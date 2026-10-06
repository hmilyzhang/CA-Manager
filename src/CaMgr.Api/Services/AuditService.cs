using CaMgr.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Services;

/// <summary>Append-only audit log writer.</summary>
public sealed class AuditService(IDbContextFactory<AppDbContext> dbf)
{
    public async Task LogAsync(string action, string objectType, string objectId, string detail, string username, string ip, bool success = true)
    {
        try
        {
            await using var db = dbf.CreateDbContext();
            db.AuditLogs.Add(new AuditLogEntity
            {
                Action = action,
                ObjectType = objectType,
                ObjectId = Trunc(objectId, 128),
                Detail = Trunc(detail, 512),
                Username = Trunc(username, 128),
                Ip = Trunc(ip, 64),
                Success = success,
            });
            db.LoginAttempts.Add(new LoginAttemptEntity { Username = username, Ip = ip, Success = action != "login_failed" });
            await db.SaveChangesAsync();
        }
        catch { /* audit must never break the request path */ }
    }

    private static string Trunc(string s, int max) => string.IsNullOrEmpty(s) ? s : (s.Length <= max ? s : s[..max]);
}

public static class AuditActions
{
    public const string Login = "login";
    public const string LoginFailed = "login_failed";
    public const string Logout = "logout";
    public const string PasswordChange = "password_change";
    public const string Revoke = "revoke";
    public const string Unrevoke = "unrevoke";
    public const string Issue = "issue";
    public const string Deny = "deny";
    public const string Resubmit = "resubmit";
    public const string Submit = "submit_request";
    public const string PublishCrl = "publish_crl";
    public const string ConfigChange = "ca_config_change";
    public const string TemplateChange = "template_change";
    public const string UserCreate = "user_create";
    public const string UserUpdate = "user_update";
    public const string UserDelete = "user_delete";
}
