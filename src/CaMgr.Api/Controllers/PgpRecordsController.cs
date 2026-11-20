using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record PgpStatusUpdateRequest(string Status, string? Notes);

[ApiController]
[Route("api/pgprecords")]
[Authorize]
public sealed class PgpRecordsController(InventoryService inventory, AuditService audit) : Controller
{
    private string Caller => User.Identity?.Name ?? "";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private bool CanSeeAll =>
        Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var r)
        && r is AppRole.Operator or AppRole.Admin or AppRole.Auditor;
    private bool IsOperator => CanSeeAll && Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var r2)
        && r2 is AppRole.Operator or AppRole.Admin;

    /// <summary>PGP key tracking records. Viewers see their own; others see all.</summary>
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? status = null)
        => Ok(await inventory.ListPgpRecordsAsync(status, Caller, CanSeeAll));

    /// <summary>Updates the tracking status of a PGP key (owner or operator/admin).</summary>
    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] PgpStatusUpdateRequest req)
    {
        var allowed = new[] { "active", "revoked", "lost", "compromised" };
        if (!allowed.Contains(req.Status)) return BadRequest(new { error = "状态无效" });
        var ok = await inventory.SetPgpStatusAsync(id, req.Status, req.Notes ?? "", Caller, IsOperator);
        if (!ok) return NotFound(new { error = "记录不存在或无权限" });
        await audit.LogAsync("pgp_status", "pgp", id.ToString(), req.Status, Caller, Ip);
        return Ok(new { message = "状态已更新" });
    }
}
