using CaMgr.Api.CaInterop;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text;

namespace CaMgr.Api.Controllers;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRoleAttribute(AppRole min) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext ctx)
    {
        var roleStr = ctx.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (!Enum.TryParse<AppRole>(roleStr, out var role) || role < min)
            ctx.Result = new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
    }
}

public record RevokeRequest(string SerialNumber, int Reason, DateTime? EffectiveFrom, string? ConfirmSerial);

[ApiController]
[Route("api/certificates")]
[Authorize]
public sealed class CertificatesController(
    CertificateService certs,
    CaDbService db,
    CaAdminService admin,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string status = "all",
        [FromQuery] string? keyword = null,
        [FromQuery] string? serial = null,
        [FromQuery] string? template = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 50,
        [FromQuery] int? before = null,
        [FromQuery] int expiringDays = 30)
    {
        if (!Enum.TryParse<CertStatusFilter>(status, true, out var st)) st = CertStatusFilter.All;
        limit = Math.Clamp(limit, 1, 200);
        var result = await certs.ListAsync(st, keyword, serial, template, from?.ToUniversalTime(), to?.ToUniversalTime(), limit, before, expiringDays);
        return Ok(result);
    }

    [HttpGet("{requestId}")]
    public async Task<IActionResult> Detail(int requestId)
    {
        var d = await certs.DetailAsync(requestId);
        return d is null ? NotFound(new { error = "请求不存在" }) : Ok(d);
    }

    [HttpGet("{requestId}/download")]
    public async Task<IActionResult> Download(int requestId, [FromQuery] string format = "cer")
    {
        var d = await certs.DetailAsync(requestId);
        if (d?.RawDerBase64 is null) return NotFound(new { error = "该请求没有已颁发的证书" });
        var der = Convert.FromBase64String(d.RawDerBase64);
        var name = $"cert-{requestId}-{d.SerialNumber}";
        return format.ToLowerInvariant() switch
        {
            "pem" => File(Encoding.ASCII.GetBytes(d.Pem ?? ""), "application/x-pem-file", $"{name}.pem"),
            _ => File(der, "application/x-x509-ca-cert", $"{name}.cer"),
        };
    }

    [HttpGet("export")]
    public async Task<IActionResult> ExportCsv(
        [FromQuery] string status = "all", [FromQuery] string? keyword = null,
        [FromQuery] string? serial = null, [FromQuery] string? template = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        if (!Enum.TryParse<CertStatusFilter>(status, true, out var st)) st = CertStatusFilter.All;
        var result = await certs.ListAsync(st, keyword, serial, template, from?.ToUniversalTime(), to?.ToUniversalTime(), 5000);

        var sb = new StringBuilder();
        sb.AppendLine("RequestId,CommonName,SerialNumber,Status,Template,Requester,NotBefore,NotAfter,RevokedAt,RevokedReason");
        foreach (var c in result.Items)
        {
            static string Esc(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",",
                c.RequestId, Esc(c.CommonName), Esc(c.SerialNumber), c.Status, Esc(c.Template),
                Esc(c.Requester),
                Esc(c.NotBefore?.ToString("yyyy-MM-dd HH:mm:ss")), Esc(c.NotAfter?.ToString("yyyy-MM-dd HH:mm:ss")),
                Esc(c.RevokedAt?.ToString("yyyy-MM-dd HH:mm:ss")), Esc(CertificateService.RevocationReasonText(c.RevokedReason))));
        }
        return File(Encoding.UTF8.GetBytes("\uFEFF" + sb), "text/csv", $"certificates-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    [HttpPost("{requestId}/revoke")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> Revoke(int requestId, [FromBody] RevokeRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.ConfirmSerial) ||
            !string.Equals(req.ConfirmSerial.Trim(), req.SerialNumber?.Trim(), StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "确认序列号不匹配，请在确认框中输入完整序列号" });

        if (req.Reason is not (>= 0 and (1 or 2 or 3 or 4 or 5 or 6 or 0 or 8)))
            return BadRequest(new { error = "无效的吊销原因" });

        try
        {
            await admin.RevokeCertificateAsync(req.SerialNumber.Trim(), req.Reason, req.EffectiveFrom);
            await audit.LogAsync(AuditActions.Revoke, "certificate", req.SerialNumber,
                $"request={requestId} reason={CertificateService.RevocationReasonText(req.Reason)} effective={req.EffectiveFrom:yyyy-MM-dd}", User_, Ip);
            return Ok(new { message = "证书已吊销，新 CRL 发布后生效" });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.Revoke, "certificate", req.SerialNumber, ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }

    [HttpPost("{requestId}/unrevoke")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> Unrevoke(int requestId, [FromQuery] string? serial = null)
    {
        var detail = await certs.DetailAsync(requestId);
        if (detail is null) return NotFound(new { error = "请求不存在" });
        if (detail.RevokedReason != CaConst.CRL_REASON_CERTIFICATE_HOLD)
            return BadRequest(new { error = "只有 certificateHold（证书冻结）原因吊销的证书才能取消吊销" });
        try
        {
            await admin.RevokeCertificateAsync(detail.SerialNumber!, CaConst.CRL_REASON_UNREVOKE, null);
            await audit.LogAsync(AuditActions.Unrevoke, "certificate", detail.SerialNumber, $"request={requestId}", User_, Ip);
            return Ok(new { message = "已取消吊销" });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.Unrevoke, "certificate", detail.SerialNumber ?? serial, ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }
}
