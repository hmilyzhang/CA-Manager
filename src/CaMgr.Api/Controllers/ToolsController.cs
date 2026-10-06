using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

[ApiController]
[Route("api/tools")]
[Authorize]
public sealed class ToolsController(PgpService pgp, AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    /// <summary>Generates an OpenPGP key pair for file encryption. Keys are returned once, never stored.</summary>
    [HttpPost("pgp/generate")]
    public async Task<IActionResult> GeneratePgp([FromBody] PgpKeyRequest req)
    {
        var (vRes, vErr) = pgp.Validate(req);
        if (vErr.Length > 0) return BadRequest(new { error = vErr });
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
