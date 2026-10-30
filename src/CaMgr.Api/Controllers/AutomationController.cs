using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CaMgr.Api.CaInterop;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record AutomationRequest(
    string CommonName,
    List<string>? San,
    string? KeyAlgorithm,     // RSA2048 | RSA4096 | ECDSA_P256 (default RSA4096)
    string Template,
    string PfxPassword);

public record AutomationRenewRequest(string SerialNumber, string PfxPassword);

[ApiController]
[Route("api/automation")]
[Authorize]
public sealed class AutomationController(
    SelfServiceCertService selfService,
    CertificateService certs,
    CaDbService db,
    AuditService audit) : Controller
{
    private string Caller => User.Identity?.Name ?? "";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

    /// <summary>Requests a new certificate (same pipeline as the UI self-service flow).</summary>
    [HttpPost("certificate")]
    public async Task<IActionResult> RequestCertificate([FromBody] AutomationRequest req)
    {
        var svcReq = new SelfServiceRequest(req.CommonName, req.San ?? [], req.KeyAlgorithm, req.Template, req.PfxPassword);
        var (vRes, vErr) = selfService.Validate(svcReq, requirePfxPassword: true);
        if (vErr.Length > 0) return BadRequest(new { error = vErr });

        var result = await selfService.GenerateAndSubmitAsync(svcReq);
        await audit.LogAsync("api_request", "automation", result.RequestId.ToString(),
            $"cn={req.CommonName} san={string.Join(",", req.San ?? [])} algo={req.KeyAlgorithm} template={req.Template} -> {result.Disposition}", Caller, Ip,
            success: result.Disposition is 3 or 4 or 5);

        return Ok(new
        {
            requestId = result.RequestId,
            disposition = result.Disposition,
            issued = result.Disposition is 3 or 4,
            pfxBase64 = result.PfxBase64,
            fileName = result.FileNameBase,
            message = result.Message,
        });
    }

    /// <summary>
    /// Re-key renewal: reads the existing certificate (by serial number) and issues a
    /// replacement with the same subject, SAN and template. The old certificate is kept.
    /// </summary>
    [HttpPost("renew")]
    public async Task<IActionResult> Renew([FromBody] AutomationRenewRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.SerialNumber))
            return BadRequest(new { error = "请提供旧证书序列号" });
        if (string.IsNullOrWhiteSpace(req.PfxPassword) || req.PfxPassword.Length < 8)
            return BadRequest(new { error = "请设置新 PFX 密码（至少 8 位）" });

        var serial = req.SerialNumber.Trim().Replace(" ", "").ToUpperInvariant();
        var row = await db.QueryAsync(
            [DbCol.SerialNumber, DbCol.CommonName, DbCol.CertificateTemplate, DbCol.RawCertificate],
            [new ViewRestriction(DbCol.SerialNumber, CaConst.CVR_SEEK_EQ, serial)],
            maxRows: 1);
        var old = row.FirstOrDefault();
        if (old is null) return NotFound(new { error = "旧证书序列号不存在" });

        var der = CaDbService.Bytes(old, DbCol.RawCertificate);
        var cert = CryptoParse.ParseCert(der);
        if (cert is null) return BadRequest(new { error = "旧证书数据无法解析" });

        var template = CaDbService.Str(old, DbCol.CertificateTemplate) ?? "";
        var cn = cert.GetNameInfo(X509NameType.DnsFromAlternativeName, false)
                 ?? cert.GetNameInfo(X509NameType.SimpleName, false)
                 ?? "";
        var sans = new List<string>();
        foreach (var ext in cert.Extensions.OfType<X509SubjectAlternativeNameExtension>())
        {
            sans.AddRange(ext.EnumerateDnsNames());
            sans.AddRange(ext.EnumerateIPAddresses().Select(ip => ip.ToString()));
        }
        sans = sans.Distinct(StringComparer.OrdinalIgnoreCase).Where(x => !string.Equals(x, cn, StringComparison.OrdinalIgnoreCase)).ToList();

        var keyAlgorithm = "RSA2048";
        var rsa = cert.GetRSAPublicKey();
        if (rsa is not null) keyAlgorithm = rsa.KeySize >= 4096 ? "RSA4096" : "RSA2048";
        else if (cert.GetECDsaPublicKey() is not null) keyAlgorithm = "ECDSA_P256";

        var request = new SelfServiceRequest(cn, sans, keyAlgorithm, template, req.PfxPassword);
        var result = await selfService.GenerateAndSubmitAsync(request);

        await audit.LogAsync("api_renew", "automation", result.RequestId.ToString(),
            $"renew of serial={serial} cn={cn} template={template} -> {result.Disposition}", Caller, Ip,
            success: result.Disposition is 3 or 4 or 5);

        return Ok(new
        {
            requestId = result.RequestId,
            disposition = result.Disposition,
            issued = result.Disposition is 3 or 4,
            pfxBase64 = result.PfxBase64,
            fileName = result.FileNameBase,
            renewedFrom = serial,
            message = result.Message,
        });
    }

    /// <summary>Status of a submitted automation request.</summary>
    [HttpGet("status/{requestId}")]
    public async Task<IActionResult> Status(int requestId)
    {
        var d = await certs.DetailAsync(requestId);
        if (d is null) return NotFound(new { error = "请求不存在" });
        return Ok(new
        {
            requestId = d.RequestId,
            disposition = d.Disposition,
            status = d.Status,
            issued = d.Disposition is 3 or 4,
            commonName = d.CommonName,
            template = d.Template,
            notAfter = d.NotAfter,
        });
    }
}
