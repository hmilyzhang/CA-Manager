using CaMgr.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Services;

public sealed record LoginOutcome(UserEntity? User, string Error, bool MustChangePassword);

/// <summary>Local account authentication with lockout. AD users are handled by LdapService.</summary>
public sealed class AuthService(IDbContextFactory<AppDbContext> dbf, ILogger<AuthService> log)
{
    public const int MaxFailedAttempts = 5;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);

    private static readonly PasswordHasher<UserEntity> Hasher = new();

    public async Task<LoginOutcome> LoginLocalAsync(string username, string password)
    {
        await using var db = dbf.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.Source == UserSource.Local);
        if (user is null)
            return new LoginOutcome(null, "用户名或密码错误", false);
        if (!user.Enabled)
            return new LoginOutcome(null, "账号已禁用", false);
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > DateTime.UtcNow)
            return new LoginOutcome(null, $"账号已锁定，请于 {user.LockedUntil.Value.ToLocalTime():HH:mm} 后重试", false);

        if (user.PasswordHash is null || Hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            user.FailedAttempts++;
            if (user.FailedAttempts >= MaxFailedAttempts)
            {
                user.LockedUntil = DateTime.UtcNow.Add(LockoutDuration);
                user.FailedAttempts = 0;
            }
            await db.SaveChangesAsync();
            return new LoginOutcome(null, "用户名或密码错误", false);
        }

        user.FailedAttempts = 0;
        user.LockedUntil = null;
        user.LastLoginAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return new LoginOutcome(user, "", user.MustChangePassword);
    }

    public async Task ChangePasswordAsync(UserEntity user, string newPassword)
    {
        await using var db = dbf.CreateDbContext();
        var fresh = await db.Users.FindAsync(user.Id);
        if (fresh is null) return;
        fresh.PasswordHash = Hasher.HashPassword(fresh, newPassword);
        fresh.MustChangePassword = false;
        await db.SaveChangesAsync();
    }

    /// <summary>Upsert of a domain user after successful LDAP bind.</summary>
    public async Task<UserEntity> UpsertAdUserAsync(string username, AppRole role, string? displayName, string? groups)
    {
        await using var db = dbf.CreateDbContext();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.Source == UserSource.Ad);
        if (user is null)
        {
            user = new UserEntity { Username = username, Source = UserSource.Ad, Role = role, DisplayName = displayName, AdGroups = groups };
            db.Users.Add(user);
        }
        else
        {
            user.Role = role;
            user.DisplayName = displayName ?? user.DisplayName;
            user.AdGroups = groups ?? user.AdGroups;
        }
        user.LastLoginAt = DateTime.UtcNow;
        user.Enabled = true;
        await db.SaveChangesAsync();
        return user;
    }

    public async Task SeedAdminAsync()
    {
        using var db = dbf.CreateDbContext();
        if (await db.Users.AnyAsync(u => u.Source == UserSource.Local)) return;

        var username = "admin";
        var password = "CaMgr@" + Guid.NewGuid().ToString("N")[..10];
        var admin = new UserEntity
        {
            Username = username,
            Role = AppRole.Admin,
            Source = UserSource.Local,
            MustChangePassword = true,
        };
        admin.PasswordHash = Hasher.HashPassword(admin, password);
        db.Users.Add(admin);
        await db.SaveChangesAsync();

        var path = Path.Combine(AppContext.BaseDirectory, "initial-admin-password.txt");
        await File.WriteAllTextAsync(path,
            $"CA-Manager 初始管理员账号\n====================\n用户名: {username}\n初始密码: {password}\n\n首次登录后系统会强制修改密码。此文件不会再次生成，请妥善保存或删除。\n生成时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
        log.LogInformation("Seeded initial admin account; password written to {Path}", path);
    }
}
