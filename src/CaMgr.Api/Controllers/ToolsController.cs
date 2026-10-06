using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

[ApiController]
[Route("api/tools")]
[Authorize]
public sealed class ToolsController(PgpService pgp, ApprovalService approvals, AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";
    private bool IsOperator =>
        Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var role)
        && role is AppRole.Operator or AppRole.Admin;

    /// <summary>Generates an OpenPGP key pair for file encryption. Viewer submissions are queued for approval; keys are returned once, never stored.</summary>
    [HttpPost("pgp/generate")]
    public async Task<IActionResult> GeneratePgp([FromBody] PgpKeyRequest req)
    {
        var (vRes, vErr) = pgp.Validate(req);
        if (vErr.Length > 0) return BadRequest(new { error = vErr });
        if (!IsOperator)
        {
            var rStr = User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
            if (Enum.TryParse<AppRole>(rStr, out var rr) && rr == AppRole.Auditor)
                return new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
            var (approvalId, keyId) = await approvals.CreatePgpAsync(User_, req);
            approvals.NotifySubmission(User_, "pgp", $"{req.Name} <{req.Email}>", req.Algorithm);
            await audit.LogAsync("approval_submit", "approval", approvalId.ToString(),
                $"type=pgp keyId={keyId} algo={req.Algorithm}", User_, Ip);
            return Ok(new { pendingApproval = true, approvalId, keyId, message = "已提交，等待审批" });
        }
        try
        {
            var result = pgp.Generate(req);
            // audit records identity + key id only — never the password or key material
            await audit.LogAsync("pgp_generate", "pgp", result.KeyId,
                $"user=\"{result.UserId}\" algo={result.Algorithm} expiry={result.ExpiresAt:yyyy-MM-dd}", User_, Ip);
            return Ok(result);
        }
        catch (Exception ex)
        {
            await audit.LogAsync("pgp_generate", "pgp", "", ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = ex.Message });
        }
    }
}
