using CaMgr.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CaMgr.Api.Controllers;

[ApiController]
[Route("api/audit")]
[Authorize]
[RequireRole(AppRole.Admin)]
public sealed class AuditController(IDbContextFactory<AppDbContext> dbf) : Controller
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? action = null,
        [FromQuery] string? user = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        await using var db = dbf.CreateDbContext();
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 10, 200);

        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.Username.Contains(user));
        if (from.HasValue) q = q.Where(a => a.At >= from.Value.ToUniversalTime());
        if (to.HasValue) q = q.Where(a => a.At <= to.Value.ToUniversalTime());

        var total = await q.CountAsync();
        var items = await q.OrderByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return Ok(new { total, page, pageSize, items });
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] string? action = null, [FromQuery] string? user = null,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        await using var db = dbf.CreateDbContext();
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(action)) q = q.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(user)) q = q.Where(a => a.Username.Contains(user));
        if (from.HasValue) q = q.Where(a => a.At >= from.Value.ToUniversalTime());
        if (to.HasValue) q = q.Where(a => a.At <= to.Value.ToUniversalTime());

        var rows = await q.OrderByDescending(a => a.Id).Take(50000).ToListAsync();
        var sb = new StringBuilder();
        sb.AppendLine("时间,用户,IP,操作,对象类型,对象,详情,结果");
        foreach (var a in rows)
        {
            static string Esc(string? s) => s is null ? "" : "\"" + s.Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",",
                Esc(a.At.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss")), Esc(a.Username), Esc(a.Ip), Esc(a.Action),
                Esc(a.ObjectType), Esc(a.ObjectId), Esc(a.Detail), a.Success ? "成功" : "失败"));
        }
        return File(Encoding.UTF8.GetBytes("\uFEFF" + sb), "text/csv", $"audit-{DateTime.Now:yyyyMMdd-HHmm}.csv");
    }

    [HttpGet("actions")]
    public async Task<IActionResult> Actions()
    {
        await using var db = dbf.CreateDbContext();
        var actions = await db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync();
        return Ok(actions);
    }
}
