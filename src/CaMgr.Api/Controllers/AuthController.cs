using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace CaMgr.Api.Controllers;

public record LoginRequest(string Username, string Password);
public record ChangePasswordRequest(string OldPassword, string NewPassword);

[ApiController]
[Route("api/auth")]
public sealed class AuthController(
    IDbContextFactory<AppDbContext> dbf,
    AuthService auth,
    AuditService audit,
    LdapAuthService ldap,
    SettingsService settings,
    ILogger<AuthController> log) : ControllerBase
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
            return BadRequest(new { error = "请输入用户名和密码" });

        LoginOutcome outcome;
        UserEntity? user;
        await using (var db = dbf.CreateDbContext())
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Username == req.Username && u.Source == UserSource.Ad);
        }
        var adEnabled = await settings.GetAsync("ldap.enabled", "false") == "true";

        if (user is not null || adEnabled)
        {
            // domain users (or any user when AD fallback is on) authenticate against LDAP first
            outcome = await ldap.TryLoginAsync(req.Username, req.Password);
            if (outcome.User is null)
            {
                // fall back to local account (covers local admin when AD is down)
                outcome = await auth.LoginLocalAsync(req.Username, req.Password);
            }
        }
        else
        {
            outcome = await auth.LoginLocalAsync(req.Username, req.Password);
        }

        if (outcome.User is null)
        {
            await audit.LogAsync(AuditActions.LoginFailed, "user", req.Username, outcome.Error, req.Username, Ip, success: false);
            return Unauthorized(new { error = outcome.Error });
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, outcome.User.Username),
            new(ClaimTypes.Role, outcome.User.Role.ToString()),
            new("source", outcome.User.Source.ToString()),
            new("uid", outcome.User.Id.ToString()),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8) });

        await audit.LogAsync(AuditActions.Login, "user", outcome.User.Username, "", outcome.User.Username, Ip);
        return Ok(new
        {
            username = outcome.User.Username,
            role = outcome.User.Role.ToString(),
            source = outcome.User.Source.ToString(),
            mustChangePassword = outcome.MustChangePassword,
        });
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var name = User.Identity?.Name ?? "";
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        await audit.LogAsync(AuditActions.Logout, "user", name, "", name, Ip);
        return Ok();
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var role = User.FindFirst(ClaimTypes.Role)?.Value ?? "Viewer";
        return Ok(new
        {
            username = User.Identity?.Name,
            role,
            source = User.FindFirst("source")?.Value,
        });
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NewPassword) || req.NewPassword.Length < 8)
            return BadRequest(new { error = "新密码至少 8 位" });

        var username = User.Identity?.Name ?? "";
        UserEntity? user;
        await using (var db = dbf.CreateDbContext())
        {
            user = await db.Users.FirstOrDefaultAsync(u => u.Username == username && u.Source == UserSource.Local);
        }
        if (user is null) return BadRequest(new { error = "域账号请在域中修改密码" });

        var check = await auth.LoginLocalAsync(username, req.OldPassword);
        if (check.User is null) return BadRequest(new { error = "原密码不正确" });

        await auth.ChangePasswordAsync(user, req.NewPassword);
        await audit.LogAsync(AuditActions.PasswordChange, "user", username, "", username, Ip);
        return Ok();
    }
}
