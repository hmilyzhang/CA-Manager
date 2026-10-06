using CaMgr.Api.CaInterop;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record SubmitCsrRequest(string Csr, string? Template, string? Attributes);

[ApiController]
[Route("api/requests")]
[Authorize]
public sealed class RequestsController(
    CertificateService certs,
    CaAdminService admin,
    CaRequestService submitter,
    SelfServiceCertService selfService,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    /// <summary>Pending/failed/denied queue.</summary>
    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] string status = "pending", [FromQuery] int limit = 50)
    {
        var filter = status switch
        {
            "pending" => CertStatusFilter.Pending,
            "denied" => CertStatusFilter.Denied,
            "failed" => CertStatusFilter.Failed,
            "all" => CertStatusFilter.All,
            _ => CertStatusFilter.Pending,
        };
        return Ok(await certs.ListAsync(filter, limit: Math.Clamp(limit, 1, 200)));
    }

    [HttpPost("{requestId}/issue")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> Issue(int requestId)
    {
        try
        {
            var disposition = await admin.ResubmitRequestAsync(requestId);
            var text = disposition switch
            {
                CaConst.CR_DISP_ISSUED => "已颁发",
                CaConst.CR_DISP_UNDER_SUBMISSION => "已重新提交，等待处理",
                CaConst.CR_DISP_DENIED => "策略模块拒绝颁发",
                _ => $"处置码 {disposition}",
            };
            await audit.LogAsync(AuditActions.Issue, "request", requestId.ToString(), disposition.ToString(), User_, Ip,
                success: disposition is CaConst.CR_DISP_ISSUED or CaConst.CR_DISP_UNDER_SUBMISSION);
            return Ok(new { disposition, message = text });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.Issue, "request", requestId.ToString(), ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }

    [HttpPost("{requestId}/deny")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> Deny(int requestId)
    {
        try
        {
            await admin.DenyRequestAsync(requestId);
            await audit.LogAsync(AuditActions.Deny, "request", requestId.ToString(), "", User_, Ip);
            return Ok(new { message = "请求已拒绝" });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.Deny, "request", requestId.ToString(), ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }

    [HttpPost("{requestId}/resubmit")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> Resubmit(int requestId) => await Issue(requestId);

    /// <summary>Submit a new CSR.</summary>
    [HttpPost("submit")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> Submit([FromBody] SubmitCsrRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Csr) || req.Csr.Trim().Length < 32)
            return BadRequest(new { error = "CSR 内容无效" });
        try
        {
            var result = await submitter.SubmitAsync(req.Csr, req.Template, req.Attributes);
            var text = result.Disposition switch
            {
                CaConst.CR_DISP_ISSUED => "已颁发",
                CaConst.CR_DISP_UNDER_SUBMISSION => "已提交，等待处理",
                CaConst.CR_DISP_DENIED => "被拒绝",
                _ => result.Message,
            };
            await audit.LogAsync(AuditActions.Submit, "request", result.RequestId.ToString(),
                $"template={req.Template} disposition={result.Disposition} message={result.Message}", User_, Ip,
                success: result.Disposition is CaConst.CR_DISP_ISSUED or CaConst.CR_DISP_UNDER_SUBMISSION);
            return Ok(new
            {
                requestId = result.RequestId,
                disposition = result.Disposition,
                message = text,
                detail = result.Message,
                lastStatus = result.LastStatus,
                certificateBase64 = result.CertificateBase64,
            });
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.Submit, "request", "", $"template={req.Template} {ex.Message}", User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }

    /// <summary>Self-service: server generates key pair + multi-SAN CSR, submits, and returns a PFX on immediate issuance.</summary>
    [HttpPost("self-service")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> SelfService([FromBody] SelfServiceRequest req)
    {
        var (vRes, vErr) = selfService.Validate(req);
        if (vErr.Length > 0) return BadRequest(new { error = vErr });
        try
        {
            var result = await selfService.GenerateAndSubmitAsync(req);
            await audit.LogAsync(AuditActions.Submit, "request", result.RequestId.ToString(),
                $"self-service CN={req.CommonName} san={string.Join(",", req.San ?? [])} key={req.KeyAlgorithm} template={req.Template} -> {result.Disposition}",
                User_, Ip,
                success: result.Disposition is 3 or 4 or 5);
            return Ok(result);
        }
        catch (Exception ex)
        {
            await audit.LogAsync(AuditActions.Submit, "request", "", $"self-service {ex.Message}", User_, Ip, success: false);
            return BadRequest(new { error = CaError.Describe(ex) });
        }
    }
}
