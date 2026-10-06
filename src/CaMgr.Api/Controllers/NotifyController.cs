using CaMgr.Api.Data;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CaMgr.Api.Controllers;

public record NotifyConfigRequest(
    bool? SmtpEnabled, string? Host, int? Port, string? Mode, string? Username, string? Password,
    string? From, string? FromName,
    string? Recipients, bool? NotifyRequester, int? ExpiringDays, string? DailyAt);

[ApiController]
[Route("api/notify")]
[Authorize]
public sealed class NotifyController(
    MailService mail,
    NotifyService notify,
    SettingsService settings,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";

    /// <summary>Full notify configuration (SMTP password masked).</summary>
    [HttpGet("config")]
    [RequireRole(AppRole.Admin)]
    public async Task<IActionResult> GetConfig()
    {
        var smtp = await mail.GetConfigAsync();
        var opt = await notify.GetOptionsAsync();
        var lastRun = await settings.GetAsync("notify.lastRun", "");
        return Ok(new
        {
            smtpEnabled = smtp.Enabled,
            host = smtp.Host,
            port = smtp.Port,
            mode = smtp.Mode,
            username = smtp.Username,
            hasPassword = !string.IsNullOrWhiteSpace(smtp.Password),
            from = smtp.From,
            fromName = smtp.FromName,
            recipients = opt.Recipients,
            notifyRequester = opt.NotifyRequester,
            expiringDays = opt.ExpiringDays,
            dailyAt = opt.DailyAt,
            lastRun,
        });
    }

    [HttpPut("config")]
    [RequireRole(AppRole.Admin)]
    public async Task<IActionResult> SaveConfig([FromBody] NotifyConfigRequest req)
    {
        if (req.SmtpEnabled == true)
        {
            if (string.IsNullOrWhiteSpace(req.Host)) return BadRequest(new { error = "SMTP 服务器不能为空" });
            if (string.IsNullOrWhiteSpace(req.From)) return BadRequest(new { error = "发件人地址不能为空" });
            if (!MailService.ValidateAddress(req.From)) return BadRequest(new { error = "发件人地址无效" });
            if (req.Mode is not (null or "none" or "starttls" or "ssl")) return BadRequest(new { error = "加密模式无效" });
        }
        if (req.Recipients is not null)
        {
            var bad = req.Recipients.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault(a => !MailService.ValidateAddress(a));
            if (bad is not null) return BadRequest(new { error = $"收件人地址无效: {bad}" });
        }
        if (req.ExpiringDays is < 1 or > 365) return BadRequest(new { error = "到期阈值需在 1-365 天" });
        if (req.DailyAt is not null && !TimeSpan.TryParse(req.DailyAt, out _)) return BadRequest(new { error = "发送时间格式应为 HH:mm" });

        var current = await mail.GetConfigAsync();
        await mail.SaveConfigAsync(new SmtpConfig
        {
            Enabled = req.SmtpEnabled ?? current.Enabled,
            Host = req.Host ?? current.Host,
            Port = req.Port ?? current.Port,
            Mode = req.Mode ?? current.Mode,
            Username = req.Username ?? current.Username,
            // empty password = keep the stored one
            Password = string.IsNullOrWhiteSpace(req.Password) ? current.Password : req.Password,
            From = req.From ?? current.From,
            FromName = req.FromName ?? current.FromName,
        }, keepExistingPassword: string.IsNullOrWhiteSpace(req.Password));

        var curOpt = await notify.GetOptionsAsync();
        await notify.SaveOptionsAsync(new NotifyOptions
        {
            Recipients = req.Recipients is null ? curOpt.Recipients
                : req.Recipients.Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            NotifyRequester = req.NotifyRequester ?? curOpt.NotifyRequester,
            ExpiringDays = req.ExpiringDays ?? curOpt.ExpiringDays,
            DailyAt = req.DailyAt ?? curOpt.DailyAt,
        });

        await audit.LogAsync(AuditActions.ConfigChange, "notify", "config", $"smtp={req.Host}:{req.Port} mode={req.Mode}", User_, Ip);
        return Ok(new { message = "通知配置已保存" });
    }

    [HttpPost("test")]
    [RequireRole(AppRole.Admin)]
    public async Task<IActionResult> Test([FromQuery] string? to)
    {
        var err = await notify.SendTestAsync(to);
        if (err is not null)
        {
            await audit.LogAsync("notify_test", "system", "test", err, User_, Ip, success: false);
            return BadRequest(new { error = err });
        }
        await audit.LogAsync("notify_test", "system", "test", $"to={to ?? "(默认收件人)"}", User_, Ip);
        return Ok(new { message = "测试邮件已发送" });
    }

    /// <summary>Run one notify scan immediately (also what the daily scheduler calls).</summary>
    [HttpPost("run-now")]
    [RequireRole(AppRole.Operator)]
    public async Task<IActionResult> RunNow()
    {
        var r = await notify.RunAsync(User_, Ip, audit);
        return Ok(new
        {
            r.Skipped,
            r.DigestSentTo,
            r.RequesterMails,
            r.ExpiringCount,
            r.CaCertExpiring,
            r.CrlStale,
            r.PendingCount,
            r.Log,
        });
    }
}
