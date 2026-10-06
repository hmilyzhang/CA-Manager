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
    ApprovalService approvals,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";
    private bool IsOperator =>
        Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var role)
        && role is AppRole.Operator or AppRole.Admin;
    private bool IsAuditor =>
        Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var role2)
        && role2 == AppRole.Auditor;

    /// <summary>Pending/failed/denied queue (global view: operator/auditor/admin).</summary>
    [HttpGet("queue")]
    [RequireRole(AppRole.Operator, AppRole.Admin, AppRole.Auditor)]
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
    [RequireRole(AppRole.Operator, AppRole.Admin)]
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
    [RequireRole(AppRole.Operator, AppRole.Admin)]
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
    [RequireRole(AppRole.Operator, AppRole.Admin)]
    public async Task<IActionResult> Resubmit(int requestId) => await Issue(requestId);

    /// <summary>Submit a new CSR. Viewer submissions go to the approval queue.</summary>
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] SubmitCsrRequest req)
    {
        if (IsAuditor) return new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
        if (string.IsNullOrWhiteSpace(req.Csr) || req.Csr.Trim().Length < 32)
            return BadRequest(new { error = "CSR 内容无效" });
        if (!IsOperator)
        {
            var rStr = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (Enum.TryParse<AppRole>(rStr, out var rr) && rr == AppRole.Auditor)
                return new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
            var approvalId = await approvals.CreateCsrAsync(User_, req.Template ?? "", req.Csr);
            approvals.NotifySubmission(User_, "csr", "(from CSR)", req.Template ?? "");
            await audit.LogAsync("approval_submit", "approval", approvalId.ToString(),
                $"type=csr template={req.Template}", User_, Ip);
            return Ok(new { pendingApproval = true, approvalId, message = "已提交，等待审批" });
        }
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

    /// <summary>Self-service. Viewer submissions are escrowed (key encrypted with the user's PFX password) and queued for approval.</summary>
    [HttpPost("self-service")]
    public async Task<IActionResult> SelfService([FromBody] SelfServiceRequest req)
    {
        if (IsAuditor) return new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
        var isViewer = !IsOperator;
        var (vRes, vErr) = selfService.Validate(req, requirePfxPassword: isViewer);
        if (vErr.Length > 0) return BadRequest(new { error = vErr });
        if (isViewer)
        {
            var approvalId = await approvals.CreateSelfServiceAsync(User_, req);
            approvals.NotifySubmission(User_, "self", req.CommonName, req.Template);
            await audit.LogAsync("approval_submit", "approval", approvalId.ToString(),
                $"type=self cn={req.CommonName} san={string.Join(",", req.San ?? [])} key={req.KeyAlgorithm} template={req.Template}", User_, Ip);
            return Ok(new { pendingApproval = true, approvalId, message = "已提交，等待审批" });
        }
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
