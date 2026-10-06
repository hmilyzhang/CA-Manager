using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Controllers;

public record CreateUserRequest(string Username, string Password, AppRole Role, string? DisplayName);
public record UpdateUserRequest(AppRole? Role, bool? Enabled, string? Password, bool? ResetMustChange);

[ApiController]
[Route("api/users")]
[Authorize]
[RequireRole(AppRole.Admin)]
public sealed class UsersController(
    IDbContextFactory<AppDbContext> dbf,
    AuthService auth,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    [HttpGet]
    public async Task<IActionResult> List()
    {
        await using var db = dbf.CreateDbContext();
        var users = await db.Users.OrderBy(u => u.Username).ToListAsync();
        return Ok(users.Select(u => new
        {
            u.Id,
            u.Username,
            u.DisplayName,
            u.Source,
            role = u.Role.ToString(),
            u.Enabled,
            u.MustChangePassword,
            u.LockedUntil,
            u.LastLoginAt,
            u.CreatedAt,
        }));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateUserRequest req)
    {
        await using var db = dbf.CreateDbContext();
        if (string.IsNullOrWhiteSpace(req.Username) || req.Username.Length > 64)
            return BadRequest(new { error = "用户名无效" });
        if (string.IsNullOrWhiteSpace(req.Password) || req.Password.Length < 8)
            return BadRequest(new { error = "密码至少 8 位" });
        if (await db.Users.AnyAsync(u => u.Username == req.Username))
            return BadRequest(new { error = "用户名已存在" });

        var user = new UserEntity
        {
            Username = req.Username,
            Role = req.Role,
            Source = UserSource.Local,
            DisplayName = req.DisplayName,
            MustChangePassword = false,
        };
        user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<UserEntity>().HashPassword(user, req.Password);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await audit.LogAsync(AuditActions.UserCreate, "user", req.Username, $"role={req.Role}", User_, Ip);
        return Ok(new { message = "用户已创建" });
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateUserRequest req)
    {
        await using var db = dbf.CreateDbContext();
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound(new { error = "用户不存在" });

        var changes = new List<string>();
        if (req.Role.HasValue && req.Role != user.Role)
        {
            if (user.Username == "admin" && req.Role < AppRole.Admin)
                return BadRequest(new { error = "不能降低内置 admin 的角色" });
            user.Role = req.Role.Value;
            changes.Add($"role={req.Role}");
        }
        if (req.Enabled.HasValue && req.Enabled != user.Enabled)
        {
            if (user.Username == "admin" && !req.Enabled.Value)
                return BadRequest(new { error = "不能禁用内置 admin" });
            user.Enabled = req.Enabled.Value;
            changes.Add($"enabled={req.Enabled}");
        }
        if (req.ResetMustChange == true)
        {
            user.MustChangePassword = true;
            changes.Add("mustChangePassword=true");
        }
        if (!string.IsNullOrWhiteSpace(req.Password))
        {
            if (req.Password.Length < 8) return BadRequest(new { error = "密码至少 8 位" });
            user.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<UserEntity>().HashPassword(user, req.Password);
            user.MustChangePassword = true;
            user.FailedAttempts = 0;
            user.LockedUntil = null;
            changes.Add("password reset");
        }
        // unlock
        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow && req.Enabled == true)
        {
            user.LockedUntil = null;
            user.FailedAttempts = 0;
        }

        await db.SaveChangesAsync();
        await audit.LogAsync(AuditActions.UserUpdate, "user", user.Username, string.Join(", ", changes), User_, Ip);
        return Ok(new { message = "已更新" });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        await using var db = dbf.CreateDbContext();
        var user = await db.Users.FindAsync(id);
        if (user is null) return NotFound(new { error = "用户不存在" });
        if (user.Username == "admin")
            return BadRequest(new { error = "不能删除内置 admin" });

        db.Users.Remove(user);
        await db.SaveChangesAsync();
        await audit.LogAsync(AuditActions.UserDelete, "user", user.Username, "", User_, Ip);
        return Ok(new { message = "已删除" });
    }
}
