using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.DirectoryServices.Protocols;

namespace CaMgr.Api.Controllers;

public record TemplateToggleRequest(string TemplateName, bool Enable);

[ApiController]
[Route("api/templates")]
[Authorize]
public sealed class TemplatesController(
    CaAdminService admin,
    SettingsService settings,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    private const string PolicyNode = @"PolicyModules\CertificateAuthority_MicrosoftDefault.Policy";

    /// <summary>Templates enabled on this CA, enriched from AD where possible.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var templates = await admin.GetTemplatesAsync();
        var adInfo = AdTemplateReader.Lookup(templates.Select(t => t.Name).ToList());
        return Ok(templates.Select(t =>
        {
            adInfo.TryGetValue(t.Name, out var ad);
            return new
            {
                name = t.Name,
                oid = t.Oid,
                displayName = ad?.DisplayName,
                validityPeriod = ad?.ValidityPeriod,
                purpose = ad?.EnhancedKeyUsage,
                enrollmentGroups = ad?.EnrollPermission,
            };
        }));
    }

    [HttpPost("toggle")]
    [RequireRole(AppRole.Admin)]
    public async Task<IActionResult> Toggle([FromBody] TemplateToggleRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.TemplateName))
            return BadRequest(new { error = "模板名不能为空" });

        try
        {
            var current = await admin.GetConfigEntryAsync(PolicyNode, "Templates");
            List<string> names = current switch
            {
                string[] a => a.ToList(),
                object[] o => o.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToList(),
                string s => s.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList(),
                _ => [],
            };

            if (req.Enable)
            {
                if (!names.Contains(req.TemplateName, StringComparer.OrdinalIgnoreCase))
                    names.Add(req.TemplateName);
            }
            else
            {
                names = names.Where(n => !string.Equals(n, req.TemplateName, StringComparison.OrdinalIgnoreCase)).ToList();
                if (names.Count == 0)
                    return BadRequest(new { error = "不能停用最后一个模板（CA 至少需要保留一个模板）" });
            }

            await admin.SetConfigEntryAsync(PolicyNode, "Templates", names.ToArray());
            await audit.LogAsync(AuditActions.TemplateChange, "template", req.TemplateName,
                req.Enable ? "启用到 CA" : "从 CA 停用", User_, Ip);
            return Ok(new { message = req.Enable ? "模板已启用" : "模板已停用", templates = names });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.TemplateChange, "template", req.TemplateName, ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = CaInterop.CaError.Describe(ex) });
        }
    }
}

/// <summary>Reads certificate template objects from AD configuration partition (best effort).</summary>
public sealed record AdTemplateInfo(string? DisplayName, string? ValidityPeriod, string? EnhancedKeyUsage, string? EnrollPermission);

public static class AdTemplateReader
{
    /// <summary>Batch lookup. Returns empty dict when AD is unreachable — CA-level info still shows.</summary>
    public static Dictionary<string, AdTemplateInfo> Lookup(List<string> templateNames)
    {
        var result = new Dictionary<string, AdTemplateInfo>(StringComparer.OrdinalIgnoreCase);
        if (templateNames.Count == 0) return result;
        try
        {
            using var conn = new LdapConnection(new LdapDirectoryIdentifier((string?)null, false, false));
            conn.SessionOptions.Sealing = true;
            conn.SessionOptions.Signing = true;
            conn.AuthType = AuthType.Negotiate;
            conn.Bind(); // machine account / service context

            var rootDse = SearchOne(conn, "rootDse", "(objectClass=*)", ["configurationNamingContext"]);
            if (rootDse is null) return result;
            var configNc = rootDse.Attributes["configurationNamingContext"]?[0]?.ToString();
            if (string.IsNullOrEmpty(configNc)) return result;

            var baseDn = $"CN=Certificate Templates,CN=Public Key Services,CN=Services,{configNc}";
            var filter = $"(|{string.Concat(templateNames.Select(n => $"(cn={Escape(n)})"))})";

            var search = new SearchRequest(baseDn, filter, System.DirectoryServices.Protocols.SearchScope.Subtree,
                ["cn", "displayName", "pKIExpirationPeriod", "pKIOverlapPeriod", "pKIExtendedKeyUsage"]);
            var response = (SearchResponse)conn.SendRequest(search);
            foreach (SearchResultEntry entry in response.Entries)
            {
                var cn = entry.Attributes["cn"]?[0]?.ToString();
                if (cn is null) continue;

                string? validity = null;
                var expBytes = entry.Attributes["pKIExpirationPeriod"]?[0] as byte[];
                if (expBytes is { Length: 8 })
                {
                    var fileTime = BitConverter.ToInt64(expBytes, 0);
                    if (fileTime < 0)
                    {
                        var neg = -fileTime;
                        validity = neg >= 3_600_000_000_000 ? $"{neg / 3_600_000_000_000} 小时"
                                 : neg >= 60_000_000_000 ? $"{neg / 60_000_000_000} 分钟"
                                 : $"{neg / 1_000_000_0} 秒";
                    }
                    else validity = $"{fileTime / 10_000_000} 秒(正数值异常)";
                }

                var ekus = (entry.Attributes["pKIExtendedKeyUsage"]?.OfType<byte[]>() ?? Enumerable.Empty<byte[]>())
                    .Select(b => CryptoParse.FriendlyOid(System.Text.Encoding.ASCII.GetString(b)))
                    .ToList();
                var ekuText = ekus.Count == 0 ? null : string.Join("; ", ekus);

                result[cn] = new AdTemplateInfo(
                    entry.Attributes["displayName"]?[0]?.ToString(),
                    validity,
                    ekuText,
                    null); // enrollment ACL extraction deferred — needs ntSecurityDescriptor parsing
            }
        }
        catch
        {
            // AD unreachable (standalone context or permission) — caller still has name/OID
        }
        return result;
    }

    private static SearchResultEntry? SearchOne(LdapConnection conn, string dn, string filter, string[] attrs)
    {
        var req = new SearchRequest(dn, filter, System.DirectoryServices.Protocols.SearchScope.Base, attrs);
        var resp = (SearchResponse)conn.SendRequest(req);
        return resp.Entries.Count > 0 ? resp.Entries[0] : null;
    }

    private static string Escape(string s) => s.Replace("\\", "\\5c").Replace("*", "\\2a").Replace("(", "\\28").Replace(")", "\\29");
}
