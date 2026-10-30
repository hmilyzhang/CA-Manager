using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record CreateApiTokenRequest(string Name);
public record RevokeApiTokenRequest(int Id);

[ApiController]
[Route("api/apitokens")]
[Authorize]
[RequireRole(AppRole.Admin)]
public sealed class ApiTokensController(ApiTokenService tokens, AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    [HttpGet]
    public async Task<IActionResult> List() => Ok(await tokens.ListAsync());

    /// <summary>Creates a per-machine token. The plaintext is returned exactly once.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateApiTokenRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || req.Name.Length > 100)
            return BadRequest(new { error = "请填写 Token 名称（机器名/用途）" });

        var created = await tokens.CreateAsync(req.Name.Trim(), User_);
        await audit.LogAsync("apitoken_create", "apitoken", created.Info.Id.ToString(),
            $"name={created.Info.Name} expires={created.Info.ExpiresAt:yyyy-MM-dd}", User_, Ip);
        return Ok(created);
    }

    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke([FromBody] RevokeApiTokenRequest req)
    {
        var ok = await tokens.RevokeAsync(req.Id);
        if (!ok) return NotFound(new { error = "Token 不存在" });
        await audit.LogAsync("apitoken_revoke", "apitoken", req.Id.ToString(), "", User_, Ip);
        return Ok(new { message = "Token 已吊销" });
    }
}
