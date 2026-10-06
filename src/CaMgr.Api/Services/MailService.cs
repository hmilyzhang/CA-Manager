using System.Linq;
using System.Text;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace CaMgr.Api.Services;

public sealed record SmtpConfig
{
    public bool Enabled { get; init; }
    public string Host { get; init; } = "";
    public int Port { get; init; } = 25;
    /// <summary>none = plain (intranet anonymous), starttls = upgrade on connect, ssl = implicit TLS (465).</summary>
    public string Mode { get; init; } = "none";
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string From { get; init; } = "";
    public string FromName { get; init; } = "CA-Manager";
}

/// <summary>
/// SMTP sender built on MailKit: supports anonymous relay and both STARTTLS / implicit-SSL encryption,
/// which the built-in System.Net.Mail.SmtpClient cannot do (no implicit TLS).
/// </summary>
public sealed class MailService(SettingsService settings, ILogger<MailService> log)
{
    public async Task<SmtpConfig> GetConfigAsync()
    {
        var all = await settings.GetAllAsync();
        string Get(string k, string d = "") => all.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : d;
        return new SmtpConfig
        {
            Enabled = Get("smtp.enabled") == "true",
            Host = Get("smtp.host"),
            Port = int.TryParse(Get("smtp.port", "25"), out var p) ? p : 25,
            Mode = Get("smtp.mode", "none"),
            Username = Get("smtp.username"),
            Password = Get("smtp.password"),
            From = Get("smtp.from"),
            FromName = Get("smtp.fromName", "CA-Manager"),
        };
    }

    public async Task SaveConfigAsync(SmtpConfig cfg, bool keepExistingPassword)
    {
        await settings.SetAsync("smtp.enabled", cfg.Enabled ? "true" : "false");
        await settings.SetAsync("smtp.host", cfg.Host ?? "");
        await settings.SetAsync("smtp.port", cfg.Port.ToString());
        await settings.SetAsync("smtp.mode", string.IsNullOrWhiteSpace(cfg.Mode) ? "none" : cfg.Mode);
        await settings.SetAsync("smtp.username", cfg.Username ?? "");
        if (!keepExistingPassword)
            await settings.SetAsync("smtp.password", cfg.Password ?? "");
        await settings.SetAsync("smtp.from", cfg.From ?? "");
        await settings.SetAsync("smtp.fromName", string.IsNullOrWhiteSpace(cfg.FromName) ? "CA-Manager" : cfg.FromName);
    }

    public static bool ValidateAddress(string? addr) =>
        !string.IsNullOrWhiteSpace(addr) && addr.Contains('@') && new System.Net.Mail.MailAddress(addr).Address == addr.Trim();

    private static string EncodeHeaderWord(string s)
    {
        if (s.All(c => c < 128)) return s;
        return $"=?utf-8?B?{Convert.ToBase64String(Encoding.UTF8.GetBytes(s))}?=";
    }

    /// <summary>Sends an HTML mail. Returns error text, or null on success.</summary>
    public async Task<string?> SendAsync(SmtpConfig cfg, IEnumerable<string> to, string subject, string htmlBody)
    {
        if (string.IsNullOrWhiteSpace(cfg.Host)) return "SMTP 服务器未配置";
        if (string.IsNullOrWhiteSpace(cfg.From)) return "发件人地址未配置";
        var recipients = to.Where(ValidateAddress).ToList();
        if (recipients.Count == 0) return "收件人地址无效";

        try
        {
            var msg = new MimeMessage();
            // pre-encode non-ASCII display names as RFC 2047 encoded-words (all-ASCII, safe on the wire)
            msg.From.Add(new MailboxAddress(EncodeHeaderWord(cfg.FromName), cfg.From));
            foreach (var r in recipients) msg.To.Add(MailboxAddress.Parse(r));
            msg.Subject = subject;
            msg.Body = new BodyBuilder { HtmlBody = htmlBody }.ToMessageBody();

            using var client = new SmtpClient();
            client.Timeout = 15000;
            var options = cfg.Mode switch
            {
                "ssl" => SecureSocketOptions.SslOnConnect,
                "starttls" => SecureSocketOptions.StartTlsWhenAvailable,
                _ => SecureSocketOptions.None,
            };
            await client.ConnectAsync(cfg.Host, cfg.Port, options);
            // anonymous intranet relay: authenticate only when credentials are present
            if (!string.IsNullOrWhiteSpace(cfg.Username))
                await client.AuthenticateAsync(cfg.Username, cfg.Password);
            await client.SendAsync(msg);
            await client.DisconnectAsync(true);
            log.LogInformation("Mail '{Subject}' sent to {Count} recipients via {Host}:{Port} ({Mode})",
                subject, recipients.Count, cfg.Host, cfg.Port, cfg.Mode);
            return null;
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Mail send failed via {Host}:{Port}", cfg.Host, cfg.Port);
            return ex.Message;
        }
    }
}
