using CaMgr.Api.Controllers;
using CaMgr.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Services;

/// <summary>Key/value settings persisted in SQLite (LDAP config, AD group mappings, misc).</summary>
public sealed class SettingsService(IDbContextFactory<AppDbContext> dbf)
{
    public async Task<string> GetAsync(string key, string @default = "")
    {
        await using var db = dbf.CreateDbContext();
        var s = await db.Settings.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key);
        return s?.Value ?? @default;
    }

    public async Task<Dictionary<string, string>> GetAllAsync()
    {
        await using var db = dbf.CreateDbContext();
        var list = await db.Settings.AsNoTracking().ToListAsync();
        return list.ToDictionary(s => s.Key, s => s.Value ?? "");
    }

    public async Task SetAsync(string key, string value)
    {
        await using var db = dbf.CreateDbContext();
        var s = await db.Settings.FirstOrDefaultAsync(x => x.Key == key);
        if (s is null) db.Settings.Add(new SettingEntity { Key = key, Value = value });
        else s.Value = value;
        await db.SaveChangesAsync();
    }
}

[ApiController]
[Route("api/settings")]
[Microsoft.AspNetCore.Authorization.Authorize]
public sealed class SettingsController(SettingsService settings, AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    [HttpGet]
    [RequireRole(Data.AppRole.Admin)]
    public async Task<IActionResult> Get() => Ok(await settings.GetAllAsync());

    [HttpPut]
    [RequireRole(Data.AppRole.Admin)]
    public async Task<IActionResult> Put([FromBody] Dictionary<string, string> values)
    {
        foreach (var (k, v) in values)
            await settings.SetAsync(k, v);
        await audit.LogAsync(AuditActions.ConfigChange, "settings", string.Join(",", values.Keys), "", User_, Ip);
        return Ok(new { message = "设置已保存" });
    }
}
