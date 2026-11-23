using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record ImportCertRequest(string Certificate);
public record UpdateInventoryRequest(string Owner, string AssignedTo, string Notes);
public record SetStatusRequest(string Status);

[ApiController]
[Route("api/inventory")]
[Authorize]
public sealed class InventoryController(InventoryService inventory, AuditService audit) : Controller
{
    private string Caller => User.Identity?.Name ?? "";
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private bool CanSeeAll =>
        Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var r)
        && r is AppRole.Operator or AppRole.Admin or AppRole.Auditor;
    private bool IsOperator => CanSeeAll && Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var r) && r is AppRole.Operator or AppRole.Admin;
    private bool IsAuditor =>
        Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var r) && r == AppRole.Auditor;

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? itemType = null, [FromQuery] string? status = null)
        => Ok(await inventory.ListAsync(itemType, status, Caller, CanSeeAll));

    /// <summary>Imports a third-party certificate (public part only) into the inventory.</summary>
    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] ImportCertRequest req)
    {
        // Auditor is read-only like on the other submit endpoints
        if (IsAuditor) return new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
        var (der, err) = DecodeCert(req.Certificate);
        if (err is not null) return BadRequest(new { error = err });
        try
        {
            var id = await inventory.ImportAsync(Caller, der);
            await audit.LogAsync("inventory_import", "inventory", id.ToString(), $"len={der.Length}", Caller, Ip);
            return Ok(new { id, message = "已导入清单" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("{id:int}/status")]
    public async Task<IActionResult> SetStatus(int id, [FromBody] SetStatusRequest req)
    {
        var allowed = new[] { "active", "expired", "revoked", "retired" };
        if (!allowed.Contains(req.Status)) return BadRequest(new { error = "状态无效" });
        var ok = await inventory.SetStatusAsync(id, req.Status, Caller, IsOperator);
        if (!ok) return NotFound(new { error = "清单项不存在或无权限" });
        await audit.LogAsync("inventory_status", "inventory", id.ToString(), req.Status, Caller, Ip);
        return Ok(new { message = "状态已更新" });
    }

    [HttpPost("{id:int}/meta")]
    public async Task<IActionResult> UpdateMeta(int id, [FromBody] UpdateInventoryRequest req)
    {
        var ok = await inventory.UpdateMetaAsync(id, req.Owner ?? "", req.AssignedTo ?? "", req.Notes ?? "", Caller, IsOperator);
        if (!ok) return NotFound(new { error = "清单项不存在或无权限" });
        await audit.LogAsync("inventory_meta", "inventory", id.ToString(), $"owner={req.Owner} assigned={req.AssignedTo}", Caller, Ip);
        return Ok(new { message = "已更新" });
    }

    /// <summary>Syncs inventory rows for all CA-issued certificates (admin/operator).</summary>
    [HttpPost("sync")]
    [RequireRole(AppRole.Operator, AppRole.Admin)]
    public async Task<IActionResult> Sync()
    {
        var created = await inventory.SyncFromCaAsync();
        await audit.LogAsync("inventory_sync", "inventory", "", $"created={created}", Caller, Ip);
        return Ok(new { created, message = $"同步完成，新建 {created} 条" });
    }

    private static (byte[] der, string? err) DecodeCert(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return (Array.Empty<byte>(), "请粘贴证书内容");
        var text = input.Trim();
        try
        {
            if (text.StartsWith("-----BEGIN"))
            {
                var b64 = System.Text.RegularExpressions.Regex.Replace(text, @"-----[A-Z0-9 ]*-----|\s", "");
                return (Convert.FromBase64String(b64), null);
            }
            return (Convert.FromBase64String(text.Replace("\r", "").Replace("\n", "")), null);
        }
        catch (FormatException)
        {
            return (Array.Empty<byte>(), "证书内容不是有效的 Base64/PEM");
        }
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? itemType = null, [FromQuery] string? status = null)
    {
        var items = await inventory.ListAsync(itemType, status, Caller, CanSeeAll);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Type,Subject,Owner,AssignedTo,Expires,Status,Identifier,Source,Notes");
        foreach (var i in items)
            sb.AppendLine($"\"{i.ItemType}\",\"{i.Subject.Replace("\"", "''")}\",\"{i.Owner}\",\"{i.AssignedTo}\",\"{i.ExpiresAt:yyyy-MM-dd}\",\"{i.Status}\",\"{i.Identifier}\",\"{i.Source}\",\"{i.Notes.Replace("\"", "''")}\"");
        return File(System.Text.Encoding.UTF8.GetBytes("﻿" + sb.ToString()), "text/csv", $"inventory-{DateTime.Now:yyyyMMdd}.csv");
    }
}
