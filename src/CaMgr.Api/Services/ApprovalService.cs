using System.Security.Cryptography;
using CaMgr.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Services;

public sealed record ApprovalDto
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string Type { get; set; } = "";
    public string Template { get; set; } = "";
    public string CommonName { get; set; } = "";
    public string San { get; set; } = "";
    public string KeyAlgorithm { get; set; } = "";
    public string Status { get; set; } = "";
    public int? RequestId { get; set; }
    public int? Disposition { get; set; }
    public string Comment { get; set; } = "";
    public string DecidedBy { get; set; } = "";
    public DateTime? DecidedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool Downloadable { get; set; }   // approved + issued + key escrow still held
    public bool Own { get; set; }            // submitted by the calling user
}

/// <summary>
/// Approval workflow for viewer submissions: the CSR/key material is held in the app
/// until an operator/admin approves; only then is the request submitted to the CA.
/// Self-service private keys are escrowed encrypted (AES-GCM with the submitter's PFX
/// password) and wiped on first download or rejection.
/// </summary>
public sealed class ApprovalService(
    IDbContextFactory<AppDbContext> dbf,
    SelfServiceCertService selfService,
    CaRequestService submitter,
    CaDbService db,
    MailService mail,
    SettingsService settings,
    ILogger<ApprovalService> log)
{
    public async Task<int> CreateCsrAsync(string username, string template, string csr)
    {
        await using var db = dbf.CreateDbContext();
        var entry = new ApprovalRequestEntity
        {
            Username = username,
            Type = "csr",
            Template = template ?? "",
            CommonName = "(from CSR)",
            CsrBase64 = NormalizeCsr(csr),
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        db.ApprovalRequests.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    public async Task<int> CreateSelfServiceAsync(string username, SelfServiceRequest req)
    {
        var (csrDer, pkcs8) = selfService.BuildCsr(req);
        var blob = SelfServiceCertService.EncryptKeyBlob(pkcs8, req.PfxPassword!);
        await using var db = dbf.CreateDbContext();
        var entry = new ApprovalRequestEntity
        {
            Username = username,
            Type = "self",
            Template = req.Template,
            CommonName = req.CommonName,
            SanCsv = string.Join(",", (req.San ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim())),
            KeyAlgorithm = req.KeyAlgorithm,
            CsrBase64 = Convert.ToBase64String(csrDer),
            KeyBlob = blob,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
        };
        db.ApprovalRequests.Add(entry);
        await db.SaveChangesAsync();
        return entry.Id;
    }

    public async Task<List<ApprovalDto>> ListAsync(string? status, string caller, bool isOperator)
    {
        await using var db = dbf.CreateDbContext();
        var q = db.ApprovalRequests.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) && status != "all")
            q = q.Where(a => a.Status == status);
        if (!isOperator)
            q = q.Where(a => a.Username == caller);
        var rows = await q.OrderByDescending(a => a.Id).Take(500).ToListAsync();
        return rows.Select(a => new ApprovalDto
        {
            Id = a.Id,
            Username = a.Username,
            Type = a.Type,
            Template = a.Template,
            CommonName = a.CommonName,
            San = a.SanCsv,
            KeyAlgorithm = a.KeyAlgorithm,
            Status = a.Status,
            RequestId = a.RequestId,
            Disposition = a.Disposition,
            Comment = a.Comment,
            DecidedBy = a.DecidedBy,
            DecidedAt = a.DecidedAt,
            CreatedAt = a.CreatedAt,
            Downloadable = a.Status == "approved" && a.Type == "self" && a.RequestId != null && a.KeyBlob != null,
            Own = a.Username == caller,
        }).ToList();
    }

    /// <summary>Approves and submits to the CA. Returns (requestId, disposition).</summary>
    public async Task<(int requestId, int disposition)> ApproveAsync(int id, string decidedBy)
    {
        await using var db = dbf.CreateDbContext();
        var row = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("审批记录不存在");
        if (row.Status != "pending")
            throw new InvalidOperationException("该申请已被处理");

        var sanAttribute = row.Type == "self"
            ? SelfServiceCertService.BuildSanAttribute(row.CommonName, row.SanCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            : null;
        var result = await submitter.SubmitAsync(row.CsrBase64, row.Template, sanAttribute);

        row.Status = "approved";
        row.RequestId = result.RequestId;
        row.Disposition = result.Disposition;
        row.DecidedBy = decidedBy;
        row.DecidedAt = DateTime.UtcNow;
        // escrowed key no longer needed when the CA denied the request outright
        if (result.Disposition is 2 or 1) row.KeyBlob = null;
        await db.SaveChangesAsync();
        return (result.RequestId, result.Disposition);
    }

    public async Task RejectAsync(int id, string decidedBy, string comment)
    {
        await using var db = dbf.CreateDbContext();
        var row = await db.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("审批记录不存在");
        if (row.Status != "pending")
            throw new InvalidOperationException("该申请已被处理");
        row.Status = "rejected";
        row.Comment = comment ?? "";
        row.DecidedBy = decidedBy;
        row.DecidedAt = DateTime.UtcNow;
        row.KeyBlob = null; // wipe escrowed key material
        await db.SaveChangesAsync();
    }

    /// <summary>Builds the PFX for an approved self-service request and wipes the escrowed key.</summary>
    public async Task<(byte[] pfx, string fileName)> BuildPfxAsync(int id, string caller, bool isOperator, string password)
    {
        await using var ctx = dbf.CreateDbContext();
        var row = await ctx.ApprovalRequests.FirstOrDefaultAsync(a => a.Id == id)
            ?? throw new InvalidOperationException("审批记录不存在");
        if (!isOperator && row.Username != caller)
            throw new InvalidOperationException("只能下载本人申请的 PFX");
        if (row.Status != "approved" || row.RequestId is null)
            throw new InvalidOperationException("该申请尚未批准");
        if (row.KeyBlob is null)
            throw new InvalidOperationException("私钥已提取或不存在（PFX 只能下载一次）");

        // fetch the issued certificate from the CA database
        var certRow = await db.GetRowAsync(row.RequestId.Value, [DbCol.RawCertificate]);
        var der = CaDbService.Bytes(certRow, DbCol.RawCertificate);
        if (der is null || der.Length == 0)
            throw new InvalidOperationException("证书尚未由 CA 颁发（可能仍在审批/待处理状态），请稍后再试");

        byte[] pkcs8;
        try { pkcs8 = SelfServiceCertService.DecryptKeyBlob(row.KeyBlob, password); }
        catch (CryptographicException) { throw new InvalidOperationException("PFX 密码不正确"); }

        var pfx = SelfServiceCertService.BuildPfxFromPkcs8(pkcs8, row.KeyAlgorithm, der, password);
        row.KeyBlob = null; // one-time download
        await ctx.SaveChangesAsync();
        return (pfx, $"{row.CommonName}.pfx");
    }

    /// <summary>Fire-and-forget approval-request mail to the fixed recipients (best effort).</summary>
    public void NotifySubmission(string username, string type, string cn, string template)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var smtp = await mail.GetConfigAsync();
                if (!smtp.Enabled) return;
                var opt = await GetRecipientsAsync();
                if (opt.Count == 0) return;
                var typeText = type == "self" ? "自助生成" : "CSR 提交";
                var html = "<div style=\"font-family:Segoe UI,Microsoft YaHei,Arial,sans-serif\">" +
                           $"<h3 style=\"color:#2563eb\">证书申请待审批</h3>" +
                           $"<p>用户 <b>{System.Net.WebUtility.HtmlEncode(username)}</b> 提交了证书申请，请登录 CA-Manager 在“审批”页面处理：</p>" +
                           "<table style=\"border-collapse:collapse;font-size:13px\" cellpadding=\"6\" border=\"1\">" +
                           $"<tr><td>类型</td><td>{typeText}</td></tr>" +
                           $"<tr><td>通用名称</td><td>{System.Net.WebUtility.HtmlEncode(cn)}</td></tr>" +
                           $"<tr><td>模板</td><td>{System.Net.WebUtility.HtmlEncode(template)}</td></tr>" +
                           "</table></div>";
                await mail.SendAsync(smtp, opt, "[CA-Manager] 证书申请待审批", html);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "approval notification mail failed");
            }
        });
    }

    private async Task<List<string>> GetRecipientsAsync()
    {
        var v = await settings.GetAsync("notify.recipients", "");
        return v.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static string NormalizeCsr(string input)
    {
        var s = input.Trim();
        if (s.Contains("-----BEGIN"))
        {
            var body = System.Text.RegularExpressions.Regex.Replace(s, @"-----[A-Z 0-9]*-----", "");
            return body.Replace("\r", "").Replace("\n", "").Trim();
        }
        return s;
    }
}
