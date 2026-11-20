using CaMgr.Api.CaInterop;
using CaMgr.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Services;

public sealed record PgpKeyRecordDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string UserId { get; set; } = "";
    public string KeyId { get; set; } = "";
    public string Fingerprint { get; set; } = "";
    public string Algorithm { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Status { get; set; } = "";
    public string Notes { get; set; } = "";
    public bool Own { get; set; }
}

public sealed record InventoryItemDto
{
    public int Id { get; set; }
    public string ItemType { get; set; } = "";
    public long RefId { get; set; }
    public string Identifier { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Owner { get; set; } = "";
    public string AssignedTo { get; set; } = "";
    public string Status { get; set; } = "";
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string Source { get; set; } = "";
    public string Notes { get; set; } = "";
    public int? CertRequestId { get; set; }     // link to the CA request detail page when x509
    public bool Expired { get; set; }
    public bool Own { get; set; }
}

/// <summary>
/// Asset inventory tracking layer. The CA database remains the source of truth for
/// X.509 certificate data; this service owns the tracking metadata (owner, assigned
/// machine/service, status, notes) plus PGP key records and imported certificates.
/// No private or public key material is ever stored here.
/// </summary>
public sealed class InventoryService(
    IDbContextFactory<AppDbContext> dbf,
    CaDbService caDb,
    ILogger<InventoryService> log)
{
    public async Task<int> AddPgpRecordAsync(string username, string userId, string keyId, string fingerprint, string algorithm, int validityYears, DateTime expiresAt)
    {
        await using var db = await dbf.CreateDbContextAsync();
        // unique fingerprint: refresh the record if the same key is generated again (same owner)
        var existing = await db.PgpKeyRecords.FirstOrDefaultAsync(p => p.Fingerprint == fingerprint);
        if (existing is not null)
        {
            existing.Username = username;
            existing.Status = "active";
            existing.Notes = existing.Notes;
            await db.SaveChangesAsync();
            return existing.Id;
        }

        var rec = new PgpKeyRecordEntity
        {
            Username = username,
            UserId = userId,
            KeyId = keyId,
            Fingerprint = fingerprint,
            Algorithm = algorithm,
            ValidityYears = validityYears,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt.ToUniversalTime(),
            Status = "active",
        };
        db.PgpKeyRecords.Add(rec);
        await db.SaveChangesAsync();

        db.InventoryItems.Add(new InventoryItemEntity
        {
            ItemType = "pgp",
            RefId = rec.Id,
            Identifier = fingerprint,
            Subject = userId,
            Owner = username,
            Status = "active",
            IssuedAt = rec.CreatedAt,
            ExpiresAt = rec.ExpiresAt,
            Source = "generated",
        });
        await db.SaveChangesAsync();
        return rec.Id;
    }

    /// <summary>Inserts an inventory row for a CA-issued certificate (idempotent by identifier).</summary>
    public async Task UpsertX509Async(int requestId, string serial, string subject, DateTime issuedAt, DateTime expiresAt, string owner, string status = "active")
    {
        await using var db = await dbf.CreateDbContextAsync();
        var identifier = serial.Trim().ToUpperInvariant();
        if (await db.InventoryItems.AnyAsync(i => i.ItemType == "x509" && i.Identifier == identifier)) return;
        db.InventoryItems.Add(new InventoryItemEntity
        {
            ItemType = "x509",
            RefId = requestId,
            Identifier = identifier,
            Subject = subject,
            Owner = owner,
            Status = status,
            IssuedAt = issuedAt.ToUniversalTime(),
            ExpiresAt = expiresAt.ToUniversalTime(),
            Source = "ca",
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Imports a third-party certificate (public part only) into the inventory.</summary>
    public async Task<int> ImportAsync(string username, byte[] der)
    {
        var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(der);
        var identifier = cert.SerialNumber;
        await using var db = await dbf.CreateDbContextAsync();
        if (await db.InventoryItems.AnyAsync(i => i.ItemType == "imported" && i.Identifier == identifier))
            throw new InvalidOperationException("该证书已在清单中");
        var item = new InventoryItemEntity
        {
            ItemType = "imported",
            RefId = 0,
            Identifier = identifier,
            Subject = cert.Subject,
            Owner = username,
            Status = cert.NotAfter > DateTime.Now ? "active" : "expired",
            IssuedAt = cert.NotBefore.ToUniversalTime(),
            ExpiresAt = cert.NotAfter.ToUniversalTime(),
            Source = "imported",
        };
        db.InventoryItems.Add(item);
        await db.SaveChangesAsync();
        return item.Id;
    }

    /// <summary>Creates inventory rows for every CA-issued certificate that does not have one yet.</summary>
    public async Task<int> SyncFromCaAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        var known = (await db.InventoryItems.Where(i => i.ItemType == "x509").Select(i => i.Identifier).ToListAsync())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var approvals = await db.ApprovalRequests.AsNoTracking()
            .Where(a => a.RequestId != null).ToDictionaryAsync(a => a.RequestId!.Value, a => a.Username);

        var rows = await caDb.QueryAsync(
            [DbCol.RequestId, DbCol.SerialNumber, DbCol.CommonName, DbCol.SubmittedWhen, DbCol.NotAfter, DbCol.Disposition, DbCol.RevokedReason],
            [new ViewRestriction(DbCol.RequestId, CaConst.CVR_SEEK_GE, 1)],
            maxRows: 20000);
        // newest first -> older ones may be beyond maxRows; still fine for incremental syncs

        int created = 0;
        foreach (var r in rows)
        {
            var serial = CaDbService.Str(r, DbCol.SerialNumber)?.Trim().ToUpperInvariant();
            if (string.IsNullOrEmpty(serial) || known.Contains(serial)) continue;
            var disp = CaDbService.Int(r, DbCol.Disposition) ?? 0;
            if (disp is not (CaConst.DB_DISP_ISSUED or CaConst.DB_DISP_REVOKED)) continue;
            var requestId = r.RequestId;
            approvals.TryGetValue(requestId, out var owner);
            var status = disp == CaConst.DB_DISP_REVOKED ? "revoked" : "active";

            db.InventoryItems.Add(new InventoryItemEntity
            {
                ItemType = "x509",
                RefId = requestId,
                Identifier = serial,
                Subject = CaDbService.Str(r, DbCol.CommonName) ?? "",
                Owner = owner ?? "",
                Status = status,
                IssuedAt = (CaDbService.Date(r, DbCol.SubmittedWhen) ?? DateTime.UtcNow).ToUniversalTime(),
                ExpiresAt = (CaDbService.Date(r, DbCol.NotAfter) ?? DateTime.UtcNow).ToUniversalTime(),
                Source = "ca",
            });
            known.Add(serial);
            created++;
        }
        await db.SaveChangesAsync();
        log.LogInformation("Inventory sync created {Count} rows", created);
        return created;
    }

    public async Task<List<InventoryItemDto>> ListAsync(string itemType, string status, string caller, bool canSeeAll)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.InventoryItems.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(itemType) && itemType != "all") q = q.Where(i => i.ItemType == itemType);
        if (!string.IsNullOrWhiteSpace(status) && status != "all") q = q.Where(i => i.Status == status);
        if (!canSeeAll) q = q.Where(i => i.Owner == caller);
        var now = DateTime.UtcNow;
        var rows = await q.OrderByDescending(i => i.ExpiresAt).Take(5000).ToListAsync();
        return rows.Select(i => new InventoryItemDto
        {
            Id = i.Id,
            ItemType = i.ItemType,
            RefId = i.RefId,
            Identifier = i.Identifier,
            Subject = i.Subject,
            Owner = i.Owner,
            AssignedTo = i.AssignedTo,
            Status = ComputeStatus(i),
            IssuedAt = i.IssuedAt,
            ExpiresAt = i.ExpiresAt,
            Source = i.Source,
            Notes = i.Notes,
            CertRequestId = i.ItemType == "x509" ? (int?)i.RefId : null,
            Expired = i.ExpiresAt.ToUniversalTime() < now && i.Status == "active",
            Own = i.Owner == caller,
        }).ToList();
    }

    private static string ComputeStatus(InventoryItemEntity i) =>
        i.Status != "active" ? i.Status
        : i.ExpiresAt.ToUniversalTime() < DateTime.UtcNow ? "expired"
        : i.Status;

    public async Task<bool> SetStatusAsync(int id, string status, string caller, bool isOperator)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;
        if (!isOperator && item.Owner != caller) return false;
        item.Status = status;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> UpdateMetaAsync(int id, string owner, string assignedTo, string notes, string caller, bool isOperator)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var item = await db.InventoryItems.FirstOrDefaultAsync(i => i.Id == id);
        if (item is null) return false;
        if (!isOperator && item.Owner != caller) return false;
        item.Owner = owner;
        item.AssignedTo = assignedTo;
        item.Notes = notes;
        item.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<List<PgpKeyRecordDto>> ListPgpRecordsAsync(string? status, string caller, bool canSeeAll)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var q = db.PgpKeyRecords.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && status != "all") q = q.Where(p => p.Status == status);
        if (!canSeeAll) q = q.Where(p => p.Username == caller);
        var rows = await q.OrderByDescending(p => p.Id).Take(2000).ToListAsync();
        return rows.Select(p => new PgpKeyRecordDto
        {
            Id = p.Id,
            Username = p.Username,
            UserId = p.UserId,
            KeyId = p.KeyId,
            Fingerprint = p.Fingerprint,
            Algorithm = p.Algorithm,
            CreatedAt = p.CreatedAt,
            ExpiresAt = p.ExpiresAt,
            Status = p.Status,
            Notes = p.Notes,
            Own = p.Username == caller,
        }).ToList();
    }

    public async Task<bool> SetPgpStatusAsync(int id, string status, string notes, string caller, bool isOperator)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var rec = await db.PgpKeyRecords.FirstOrDefaultAsync(p => p.Id == id);
        if (rec is null) return false;
        if (!isOperator && rec.Username != caller) return false;
        rec.Status = status;
        if (notes.Length > 0) rec.Notes = notes;
        await db.SaveChangesAsync();
        return true;
    }
}