using System.Security.Cryptography;
using CaMgr.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Services;

public sealed record ApiTokenInfo
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool Enabled { get; set; }
    public bool Expired { get; set; }
}

public sealed record CreatedToken(ApiTokenInfo Info, string Plaintext);

/// <summary>
/// API tokens for machine automation: per-machine bearer tokens with a fixed
/// Operator role and a 1-year expiry. Only the SHA256 hash is stored - the
/// plaintext is returned exactly once at creation.
/// </summary>
public sealed class ApiTokenService(IDbContextFactory<AppDbContext> dbf, ILogger<ApiTokenService> log)
{
    public const string Prefix = "cam_";
    public static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(365);

    public static string Hash(string plaintext)
    {
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(plaintext));
        return Convert.ToHexString(bytes);
    }

    public async Task<CreatedToken> CreateAsync(string name, string createdBy)
    {
        var plaintext = Prefix + Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();
        var now = DateTime.UtcNow;
        await using var db = await dbf.CreateDbContextAsync();
        var entity = new ApiTokenEntity
        {
            Name = name,
            TokenHash = Hash(plaintext),
            Role = (int)AppRole.Operator,
            CreatedBy = createdBy,
            CreatedAt = now,
            ExpiresAt = now.Add(TokenLifetime),
            Enabled = true,
        };
        db.ApiTokens.Add(entity);
        await db.SaveChangesAsync();
        log.LogInformation("API token created: {Name} by {By}", name, createdBy);
        return new CreatedToken(ToInfo(entity, expired: false), plaintext);
    }

    /// <summary>Validates a Bearer token; returns the token entity or null.</summary>
    public async Task<ApiTokenEntity?> ValidateAsync(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext) || !plaintext.StartsWith(Prefix)) return null;
        var hash = Hash(plaintext);
        await using var db = await dbf.CreateDbContextAsync();
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token is null || !token.Enabled) return null;
        if (token.ExpiresAt.ToUniversalTime() < DateTime.UtcNow) return null;

        // touch last-used (best effort)
        try
        {
            token.LastUsedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        catch { /* concurrent updates are fine to lose */ }
        return token;
    }

    public async Task<List<ApiTokenInfo>> ListAsync()
    {
        await using var db = await dbf.CreateDbContextAsync();
        var rows = await db.ApiTokens.AsNoTracking().OrderByDescending(t => t.Id).ToListAsync();
        var now = DateTime.UtcNow;
        return rows.Select(t => ToInfo(t, t.ExpiresAt.ToUniversalTime() < now)).ToList();
    }

    public async Task<bool> RevokeAsync(int id)
    {
        await using var db = await dbf.CreateDbContextAsync();
        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id);
        if (token is null) return false;
        token.Enabled = false;
        token.ExpiresAt = DateTime.UtcNow; // expired -> validation fails
        await db.SaveChangesAsync();
        return true;
    }

    private static ApiTokenInfo ToInfo(ApiTokenEntity t, bool expired) => new()
    {
        Id = t.Id,
        Name = t.Name,
        CreatedBy = t.CreatedBy,
        CreatedAt = t.CreatedAt,
        ExpiresAt = t.ExpiresAt,
        LastUsedAt = t.LastUsedAt,
        Enabled = t.Enabled,
        Expired = expired,
    };
}
