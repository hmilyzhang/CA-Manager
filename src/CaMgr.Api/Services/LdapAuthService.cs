using System.DirectoryServices.Protocols;
using System.Security.Claims;
using CaMgr.Api.Data;

namespace CaMgr.Api.Services;

/// <summary>
/// AD authentication: LDAP bind + group-based role mapping.
/// Settings: ldap.host (optional override), ldap.adminGroup / ldap.operatorGroup (DN or name),
/// unmapped domain users get Viewer when ldap.allowUnmapped=true, otherwise rejected.
/// </summary>
public sealed class LdapAuthService(
    SettingsService settings,
    AuthService auth,
    ILogger<LdapAuthService> log)
{
    public async Task<LoginOutcome> TryLoginAsync(string username, string password)
    {
        try
        {
            var host = await settings.GetAsync("ldap.host", "");
            var identifier = string.IsNullOrWhiteSpace(host)
                ? new LdapDirectoryIdentifier((string?)null, false, false)   // default (this DC / domain)
                : new LdapDirectoryIdentifier(host);
            using var conn = new LdapConnection(identifier)
            {
                AuthType = AuthType.Negotiate,
            };
            conn.SessionOptions.Sealing = true;
            conn.SessionOptions.Signing = true;
            conn.Timeout = TimeSpan.FromSeconds(8);

            var domain = await settings.GetAsync("ldap.domain", "");
            var bindUser = string.IsNullOrWhiteSpace(domain) || username.Contains('\\') || username.Contains('@')
                ? username
                : $"{domain}\\{username}";
            conn.Bind(new System.Net.NetworkCredential(bindUser, password));

            // bind succeeded → resolve groups for role mapping
            var groups = GetGroups(conn, bindUser);
            var role = await MapRoleAsync(groups);
            var allowUnmapped = await settings.GetAsync("ldap.allowUnmapped", "true") == "true";
            if (role is null && !allowUnmapped)
                return new LoginOutcome(null, "域账号未配置访问权限", false);

            var display = ResolveDisplayName(conn, bindUser);
            var user = await auth.UpsertAdUserAsync(
                username.Contains('\\') ? username.Split('\\')[1] : username.Split('@')[0],
                role ?? AppRole.Viewer,
                display,
                string.Join(",", groups));
            return new LoginOutcome(user, "", user.MustChangePassword);
        }
        catch (Exception ex)
        {
            log.LogDebug(ex, "LDAP bind failed for {User}", username);
            return new LoginOutcome(null, "域账号验证失败", false);
        }
    }

    private static List<string> GetGroups(LdapConnection conn, string bindUser)
    {
        var groups = new List<string>();
        try
        {
            var rootDse = new SearchRequest("rootDse", "(objectClass=*)", SearchScope.Base, ["defaultNamingContext"]);
            var root = (SearchResponse)conn.SendRequest(rootDse);
            var nc = root.Entries[0].Attributes["defaultNamingContext"]?[0]?.ToString();
            if (nc is null) return groups;

            var account = bindUser.Contains('\\') ? bindUser.Split('\\')[1] : bindUser.Split('@')[0];
            var search = new SearchRequest(nc, $"(&(objectClass=user)(sAMAccountName={Escape(account)}))", SearchScope.Subtree,
                ["memberOf", "distinguishedName"]);
            var resp = (SearchResponse)conn.SendRequest(search);
            if (resp.Entries.Count > 0)
            {
                foreach (var g in resp.Entries[0].Attributes["memberOf"]?.OfType<string>() ?? [])
                    groups.Add(g);
            }
        }
        catch { /* group lookup is best effort */ }
        return groups;
    }

    private static string? ResolveDisplayName(LdapConnection conn, string bindUser)
    {
        try
        {
            var rd = new SearchRequest("rootDse", "(objectClass=*)", SearchScope.Base, ["defaultNamingContext"]);
            var nc = ((SearchResponse)conn.SendRequest(rd)).Entries[0].Attributes["defaultNamingContext"][0].ToString();
            var account = bindUser.Contains('\\') ? bindUser.Split('\\')[1] : bindUser.Split('@')[0];
            var search = new SearchRequest(nc, $"(&(objectClass=user)(sAMAccountName={Escape(account)}))", SearchScope.Subtree, ["displayName"]);
            var resp = (SearchResponse)conn.SendRequest(search);
            return resp.Entries.Count > 0 ? resp.Entries[0].Attributes["displayName"]?[0]?.ToString() : null;
        }
        catch { return null; }
    }

    private async Task<AppRole?> MapRoleAsync(List<string> groups)
    {
        var adminGroup = await settings.GetAsync("ldap.adminGroup", "");   // no implicit Domain Admins mapping (PAM-managed)
        var opGroup = await settings.GetAsync("ldap.operatorGroup", "");
        foreach (var g in groups)
        {
            var cn = g.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? g[3..g.IndexOf(',')] : g;
            if (string.Equals(cn, adminGroup, StringComparison.OrdinalIgnoreCase)) return AppRole.Admin;
        }
        if (opGroup.Length > 0)
        {
            foreach (var g in groups)
            {
                var cn = g.StartsWith("CN=", StringComparison.OrdinalIgnoreCase) ? g[3..g.IndexOf(',')] : g;
                if (string.Equals(cn, opGroup, StringComparison.OrdinalIgnoreCase)) return AppRole.Operator;
            }
        }
        return null;
    }

    private static string Escape(string s) => s.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29");
}
