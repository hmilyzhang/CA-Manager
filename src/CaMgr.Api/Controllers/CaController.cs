using CaMgr.Api.CaInterop;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CaMgr.Api.Controllers;

public record CrlPeriodRequest(int BaseUnits, string BasePeriod, int DeltaUnits, string DeltaPeriod);
public record PublishCrlRequest(bool Base, bool Delta);

[ApiController]
[Route("api/ca")]
[Authorize]
public sealed class CaController(
    CaAdminService admin,
    CaContext ca,
    CertificateService certs,
    SettingsService settings,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    /// <summary>CA overview: name, type, health, CA certificate, CRL windows.</summary>
    [HttpGet("info")]
    public async Task<IActionResult> Info()
    {
        try
        {
            var name = (await admin.GetCaPropertyAsync(CaConst.CR_PROP_CANAME, 0, CaConst.PROPTYPE_STRING) as string) ?? "";
            var dns = (await admin.GetCaPropertyAsync(CaConst.CR_PROP_DNSNAME, 0, CaConst.PROPTYPE_STRING) as string) ?? "";
            var caTypeLong = await admin.GetCaPropertyAsync(CaConst.CR_PROP_CATYPE, 0, CaConst.PROPTYPE_LONG);
            int caType = caTypeLong is int i ? i : 0; // 0=enterprise root,1=enterprise subordinate,3=standalone root,4=standalone sub
            var policy = (await admin.GetCaPropertyAsync(CaConst.CR_PROP_POLICYDESCRIPTION, 0, CaConst.PROPTYPE_STRING) as string) ?? "";
            var version = (await admin.GetCaPropertyAsync(CaConst.CR_PROP_FILEVERSION, 0, CaConst.PROPTYPE_STRING) as string) ?? "";

            var caCertDer = await admin.GetCaPropertyBytesAsync(CaConst.CR_PROP_CASIGCERT, 0);
            var caCert = CryptoParse.ParseCert(caCertDer);

            var crlPeriodUnits = await admin.GetConfigEntryAsync("", "CRLPeriodUnits");
            var crlPeriod = await admin.GetConfigEntryAsync("", "CRLPeriod") as string;
            var crlDeltaUnits = await admin.GetConfigEntryAsync("", "CRLDeltaPeriodUnits");
            var crlDeltaPeriod = await admin.GetConfigEntryAsync("", "CRLDeltaPeriod") as string;
            var cdpUrls = await admin.GetCaPropertyAsync(CaConst.CR_PROP_CERTCDPURLS, 0, CaConst.PROPTYPE_STRING);
            var aiaUrls = await admin.GetCaPropertyAsync(CaConst.CR_PROP_CERTAIAURLS, 0, CaConst.PROPTYPE_STRING);

            return Ok(new
            {
                config = ca.Config,
                name,
                dnsName = dns,
                typeCode = caType,
                policyModule = policy,
                version,
                alive = true,
                caCertificate = caCert is null ? null : new
                {
                    subject = caCert.Subject,
                    notBefore = caCert.NotBefore,
                    notAfter = caCert.NotAfter,
                    thumbprint = caCert.Thumbprint,
                    serial = caCert.SerialNumber,
                },
                crl = new
                {
                    periodUnits = crlPeriodUnits,
                    period = crlPeriod,
                    deltaUnits = crlDeltaUnits,
                    deltaPeriod = crlDeltaPeriod,
                },
                cdp = AsStringArray(cdpUrls),
                aia = AsStringArray(aiaUrls),
            });
        }
        catch (Exception ex)
        {
            return Ok(new { config = ca.Config, alive = false, error = CaError.Describe(ex) });
        }
    }

    private static string[] AsStringArray(object? v) => v switch
    {
        string[] a => a,
        object[] o => o.Select(x => x?.ToString() ?? "").Where(s => s.Length > 0).ToArray(),
        string s => [s],
        _ => [],
    };

    [HttpGet("crl")]
    public async Task<IActionResult> GetCrl([FromQuery] string kind = "base")
    {
        var propId = kind == "delta" ? CaConst.CR_PROP_DELTACRL : CaConst.CR_PROP_BASECRL;
        var raw = await admin.GetCaPropertyBytesAsync(propId, 0);
        if (raw is null) return NotFound(new { error = "CRL 不可用" });
        return File(raw, "application/pkix-crl", $"{(kind == "delta" ? "delta" : "base")}-crl-{DateTime.Now:yyyyMMdd}.crl");
    }

    [HttpGet("crl/detail")]
    public async Task<IActionResult> CrlDetail([FromQuery] string kind = "base")
    {
        var propId = kind == "delta" ? CaConst.CR_PROP_DELTACRL : CaConst.CR_PROP_BASECRL;
        var raw = await admin.GetCaPropertyBytesAsync(propId, 0);
        if (raw is null) return NotFound(new { error = "CRL 不可用" });
        var info = CryptoParse.ParseCrl(raw);
        return Ok(new
        {
            kind,
            issuer = info?.Issuer,
            thisUpdate = info?.ThisUpdate,
            nextUpdate = info?.NextUpdate,
            revokedCount = info?.RevokedCount ?? 0,
            signatureAlgorithm = info?.SignatureAlgorithm,
            entries = info?.Entries.Take(500).Select(e => new
            {
                serial = e.Serial,
                revokedOn = e.RevokedOn,
                reason = e.Reason,
                reasonText = CertificateService.RevocationReasonText(e.Reason),
            }),
        });
    }

    [HttpPost("crl/publish")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> PublishCrl([FromBody] PublishCrlRequest req)
    {
        try
        {
            await admin.PublishCrlsAsync(req.Base, req.Delta);
            await audit.LogAsync(AuditActions.PublishCrl, "crl", req.Base ? (req.Delta ? "base+delta" : "base") : "delta", "", User_, Ip);
            return Ok(new { message = "CRL 发布已触发" });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.PublishCrl, "crl", "", ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }

    [HttpPost("crl/period")]
    [RequireRole(AppRole.Admin)]
    public async Task<IActionResult> SetCrlPeriod([FromBody] CrlPeriodRequest req)
    {
        if (req.BaseUnits < 1 || req.BaseUnits > 52) return BadRequest(new { error = "基础 CRL 周期需在 1-52 之间" });
        if (req.DeltaUnits is < 0 or > 52) return BadRequest(new { error = "Delta CRL 周期需在 0-52 之间（0=禁用）" });
        var validPeriods = new[] { "Hours", "Days", "Weeks", "Months", "Years" };
        if (!validPeriods.Contains(req.BasePeriod) || !validPeriods.Contains(req.DeltaPeriod))
            return BadRequest(new { error = "周期单位无效" });

        try
        {
            var old = (await admin.GetConfigEntryAsync("", "CRLPeriodUnits"), await admin.GetConfigEntryAsync("", "CRLDeltaPeriodUnits"));
            await admin.SetConfigEntryAsync("", "CRLPeriodUnits", req.BaseUnits);
            await admin.SetConfigEntryAsync("", "CRLPeriod", req.BasePeriod);
            if (req.DeltaUnits == 0)
            {
                // 0 disables delta publication
                await admin.SetConfigEntryAsync("", "CRLDeltaPeriodUnits", 0);
                await admin.SetConfigEntryAsync("", "CRLDeltaPeriod", req.DeltaPeriod);
            }
            else
            {
                await admin.SetConfigEntryAsync("", "CRLDeltaPeriodUnits", req.DeltaUnits);
                await admin.SetConfigEntryAsync("", "CRLDeltaPeriod", req.DeltaPeriod);
            }
            await audit.LogAsync(AuditActions.ConfigChange, "ca", "CRLPeriod",
                $"base: {old.Item1} -> {req.BaseUnits} {req.BasePeriod}; delta: {old.Item2} -> {req.DeltaUnits} {req.DeltaPeriod}", User_, Ip);
            return Ok(new { message = "CRL 周期已更新（下次发布起生效）" });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.ConfigChange, "ca", "CRLPeriod", ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }
}
