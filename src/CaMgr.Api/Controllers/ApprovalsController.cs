using System.Security.Claims;
using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record PfxDownloadRequest(string Password);

[ApiController]
[Route("api/approvals")]
[Authorize]
public sealed class ApprovalsController(
    ApprovalService approvals,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";
    private bool IsOperator =>
        Enum.TryParse<AppRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var role) && role >= AppRole.Operator;
    private bool CanSeeAll =>
        Enum.TryParse<AppRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var role) && role >= AppRole.Operator;

    /// <summary>List approval requests. Operators/admins see all; viewers see their own.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status = null)
    {
        var items = await approvals.ListAsync(status, User_, IsOperator);
        return Ok(items);
    }

    [HttpPost("{id:int}/approve")]
    [RequireRole(AppRole.Operator, AppRole.Admin)]
    public async Task<IActionResult> Approve(int id)
    {
        try
        {
            var (requestId, disposition) = await approvals.ApproveAsync(id, User_);
            var text = disposition switch
            {
                3 => "已颁发",
                5 => "已提交 CA，进入待处理",
                2 => "CA 策略模块拒绝",
                _ => $"处置码 {disposition}",
            };
            await audit.LogAsync("approval_approve", "approval", id.ToString(),
                $"requestId={requestId} disposition={disposition}", User_, Ip);
            return Ok(new { requestId, disposition, message = text });
        }
        catch (Exception ex)
        {
            await audit.LogAsync("approval_approve", "approval", id.ToString(), ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/reject")]
    [RequireRole(AppRole.Operator, AppRole.Admin)]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectRequest req)
    {
        try
        {
            await approvals.RejectAsync(id, User_, req.Comment ?? "");
            await audit.LogAsync("approval_reject", "approval", id.ToString(), req.Comment ?? "", User_, Ip);
            return Ok(new { message = "已拒绝" });
        }
        catch (Exception ex)
        {
            await audit.LogAsync("approval_reject", "approval", id.ToString(), ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>One-time PFX download for an approved self-service request (escrowed key wiped after).</summary>
    [HttpPost("{id:int}/pfx")]
    public async Task<IActionResult> DownloadPfx(int id, [FromBody] PfxDownloadRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Password)) return BadRequest(new { error = "请输入 PFX 密码" });
        try
        {
            var (pfx, fileName) = await approvals.BuildPfxAsync(id, User_, IsOperator, req.Password);
            await audit.LogAsync("approval_pfx", "approval", id.ToString(), $"file={fileName}", User_, Ip);
            return File(pfx, "application/x-pkcs12", fileName);
        }
        catch (Exception ex)
        {
            await audit.LogAsync("approval_pfx", "approval", id.ToString(), ex.Message, User_, Ip, success: false);
            return BadRequest(new { error = ex.Message });
        }
    }
}

public record RejectRequest(string? Comment);
