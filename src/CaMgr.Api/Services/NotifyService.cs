using System.Security.Cryptography.X509Certificates;
using System.Text;
using CaMgr.Api.CaInterop;

namespace CaMgr.Api.Services;

public sealed record NotifyOptions
{
    public List<string> Recipients { get; init; } = [];
    public bool NotifyRequester { get; init; }
    public int ExpiringDays { get; init; } = 30;
    public string DailyAt { get; init; } = "08:00";
}

public sealed record NotifyResult
{
    public bool Skipped { get; init; }
    public string? Error { get; init; }
    public int DigestSentTo { get; init; }
    public int RequesterMails { get; init; }
    public int ExpiringCount { get; init; }
    public bool CaCertExpiring { get; init; }
    public bool CrlStale { get; init; }
    public int PendingCount { get; init; }
    public List<string> Log { get; init; } = [];
}

/// <summary>Scans the four notification events, sends the daily digest to the fixed recipient list and (optionally) individual mails to requesters.</summary>
public sealed class NotifyService(
    MailService mail,
    SettingsService settings,
    CertificateService certs,
    CaAdminService admin,
    CaDbService db,
    ILogger<NotifyService> log)
{
    public async Task<NotifyOptions> GetOptionsAsync()
    {
        var all = await settings.GetAllAsync();
        string Get(string k, string d = "") => all.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : d;
        return new NotifyOptions
        {
            Recipients = Get("notify.recipients").Split(',', ';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            NotifyRequester = Get("notify.notifyRequester") == "true",
            ExpiringDays = int.TryParse(Get("notify.expiringDays", "30"), out var d) && d > 0 ? d : 30,
            DailyAt = Get("notify.dailyAt", "08:00"),
        };
    }

    public async Task SaveOptionsAsync(NotifyOptions o)
    {
        await settings.SetAsync("notify.recipients", string.Join(",", o.Recipients));
        await settings.SetAsync("notify.notifyRequester", o.NotifyRequester ? "true" : "false");
        await settings.SetAsync("notify.expiringDays", o.ExpiringDays.ToString());
        await settings.SetAsync("notify.dailyAt", string.IsNullOrWhiteSpace(o.DailyAt) ? "08:00" : o.DailyAt);
    }

    /// <summary>Runs one scan+send cycle (daily scheduler and manual trigger both land here).</summary>
    public async Task<NotifyResult> RunAsync(string actor, string ip, AuditService? audit = null)
    {
        var smtp = await mail.GetConfigAsync();
        var opt = await GetOptionsAsync();
        var result = new NotifyResult();

        if (!smtp.Enabled) return result with { Skipped = true, Log = ["SMTP 未启用"] };
        if (opt.Recipients.Count == 0 && !opt.NotifyRequester) return result with { Skipped = true, Log = ["未配置收件人"] };

        var notes = new List<string>();

        // ---- event 1: certificates expiring within threshold ----
        var expiring = await ListExpiringWithRequesterAsync(opt.ExpiringDays);
        // ---- event 2: CA certificate nearing expiry ----
        bool caCertExpiring = false;
        string? caCertText = null;
        try
        {
            var caCert = CryptoParse.ParseCert(await admin.GetCaPropertyBytesAsync(CaConst.CR_PROP_CASIGCERT, 0));
            if (caCert is not null)
            {
                var left = (caCert.NotAfter - DateTime.Now).TotalDays;
                if (left < 90)
                {
                    caCertExpiring = true;
                    caCertText = $"{caCert.Subject} — {caCert.NotAfter:yyyy-MM-dd} ({(int)left} d)";
                }
            }
        }
        catch (Exception ex) { notes.Add("CA 证书读取失败: " + ex.Message); }

        // ---- event 3: base CRL stale / expiring within 1 day ----
        bool crlStale = false;
        string? crlText = null;
        try
        {
            var crl = CryptoParse.ParseCrl(await admin.GetCaPropertyBytesAsync(CaConst.CR_PROP_BASECRL, 0));
            if (crl?.NextUpdate is { } next)
            {
                var left = (next - DateTime.UtcNow).TotalHours;
                if (left < 24)
                {
                    crlStale = true;
                    crlText = left < 0
                        ? $"基础 CRL 已过期 {-(int)left} 小时（下次更新 {next:yyyy-MM-dd HH:mm} UTC）"
                        : $"基础 CRL 将于 {next:yyyy-MM-dd HH:mm} UTC 过期（剩余 {(int)left} 小时）";
                }
            }
        }
        catch (Exception ex) { notes.Add("CRL 读取失败: " + ex.Message); }

        // ---- event 4: pending requests backlog ----
        int pendingCount = 0;
        try
        {
            var pending = await certs.ListAsync(CertStatusFilter.Pending, limit: 500);
            pendingCount = pending.TotalFetched;
        }
        catch (Exception ex) { notes.Add("待处理请求读取失败: " + ex.Message); }

        bool anyEvent = expiring.Count > 0 || caCertExpiring || crlStale || pendingCount > 0;

        var r = result with
        {
            ExpiringCount = expiring.Count,
            CaCertExpiring = caCertExpiring,
            CrlStale = crlStale,
            PendingCount = pendingCount,
        };

        // ---- digest mail to the fixed recipient list ----
        int digestTo = 0;
        if (anyEvent && opt.Recipients.Count > 0)
        {
            var html = BuildDigestHtml(expiring, caCertText, crlText, pendingCount, opt.ExpiringDays, notes);
            var err = await mail.SendAsync(smtp, opt.Recipients, "[CA-Manager] 每日 PKI 通知", html);
            if (err is null) digestTo = opt.Recipients.Count;
            else notes.Add("汇总邮件发送失败: " + err);
        }

        // ---- individual mails to certificate requesters ----
        int requesterMails = 0;
        if (opt.NotifyRequester && expiring.Count > 0)
        {
            foreach (var g in expiring.Where(x => !string.IsNullOrWhiteSpace(x.Email)).GroupBy(x => x.Email!.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var html = BuildRequesterHtml(g.Key, g.ToList(), opt.ExpiringDays);
                var err = await mail.SendAsync(smtp, [g.Key], "[CA-Manager] 您的证书即将到期", html);
                if (err is null) requesterMails++;
                else notes.Add($"申请者邮件({g.Key})失败: " + err);
            }
        }

        r = r with { DigestSentTo = digestTo, RequesterMails = requesterMails, Log = notes };
        if (audit is not null)
        {
            await audit.LogAsync("notify_send", "system", "scan",
                $"digestTo={digestTo} requesterMails={requesterMails} expiring={expiring.Count} caCert={caCertExpiring} crlStale={crlStale} pending={pendingCount}",
                actor, ip);
        }
        await settings.SetAsync("notify.lastRun", DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        return r;
    }

    /// <summary>Sends a test mail to one address (or the first configured recipient).</summary>
    public async Task<string?> SendTestAsync(string? to)
    {
        var smtp = await mail.GetConfigAsync();
        if (!smtp.Enabled) return "SMTP 未启用";
        if (string.IsNullOrWhiteSpace(to))
        {
            var opt = await GetOptionsAsync();
            to = opt.Recipients.FirstOrDefault();
        }
        if (string.IsNullOrWhiteSpace(to)) return "未指定测试收件人";
        var html = "<div style=\"font-family:Segoe UI,Arial,sans-serif\">" +
                   "<h3>CA-Manager 测试邮件</h3>" +
                   $"<p>这是一封测试邮件。如果您收到它，说明 SMTP 配置正确。</p>" +
                   $"<p style=\"color:#888;font-size:12px\">{DateTime.Now:yyyy-MM-dd HH:mm:ss} · {smtp.Host}:{smtp.Port} ({smtp.Mode})</p></div>";
        return await mail.SendAsync(smtp, [to], "[CA-Manager] 测试邮件", html);
    }

    private sealed record ExpiringRow(int RequestId, string? CommonName, DateTime NotAfter, string? Template, string? Email);

    private async Task<List<ExpiringRow>> ListExpiringWithRequesterAsync(int days)
    {
        var cutoff = DateTime.Now.AddDays(days);
        var baseCols = new[] { DbCol.RequestId, DbCol.CommonName, DbCol.NotAfter, DbCol.CertificateTemplate, DbCol.Disposition };
        var emailCol = "Request.EMail"; // schema-verified; keep defensive fallback
        List<CaRow> rows;
        try
        {
            rows = await db.QueryAsync(baseCols.Append(emailCol).ToList(),
                [new ViewRestriction(DbCol.Disposition, CaConst.CVR_SEEK_EQ, CaConst.DB_DISP_ISSUED)], 2000);
        }
        catch (InvalidOperationException)
        {
            rows = await db.QueryAsync(baseCols.ToList(),
                [new ViewRestriction(DbCol.Disposition, CaConst.CVR_SEEK_EQ, CaConst.DB_DISP_ISSUED)], 2000);
        }
        return rows
            .Select(r => new ExpiringRow(
                r.RequestId,
                CaDbService.Str(r, DbCol.CommonName),
                CaDbService.Date(r, DbCol.NotAfter) ?? DateTime.MinValue,
                CaDbService.Str(r, DbCol.CertificateTemplate),
                CaDbService.Str(r, emailCol)))
            .Where(x => x.NotAfter >= DateTime.Now && x.NotAfter <= cutoff)
            .OrderBy(x => x.NotAfter)
            .ToList();
    }

    private static string Escape(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    private static string Table(IEnumerable<ExpiringRow> rows)
    {
        var sb = new StringBuilder();
        sb.Append("<table style=\"border-collapse:collapse;font-size:13px\" cellpadding=\"6\" border=\"1\">");
        sb.Append("<tr style=\"background:#f0f2f5\"><th>请求ID</th><th>通用名称</th><th>模板</th><th>到期时间</th><th>剩余天数</th></tr>");
        foreach (var x in rows)
        {
            var left = (int)(x.NotAfter - DateTime.Now).TotalDays;
            var color = left <= 7 ? "#f56c6c" : left <= 15 ? "#e6a23c" : "#333";
            sb.Append($"<tr><td>{x.RequestId}</td><td>{Escape(x.CommonName)}</td><td>{Escape(x.Template)}</td>" +
                      $"<td>{x.NotAfter:yyyy-MM-dd}</td><td style=\"color:{color};font-weight:600\">{left}</td></tr>");
        }
        sb.Append("</table>");
        return sb.ToString();
    }

    private static string BuildDigestHtml(List<ExpiringRow> expiring, string? caCert, string? crl, int pending, int days, List<string> notes)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Microsoft YaHei,Arial,sans-serif;max-width:800px\">");
        sb.Append($"<h3 style=\"color:#2563eb\">CA-Manager 每日 PKI 通知 · {DateTime.Now:yyyy-MM-dd}</h3>");

        if (expiring.Count > 0)
        {
            sb.Append($"<h4>① {days} 天内到期证书（{expiring.Count} 张）</h4>");
            sb.Append(Table(expiring));
        }
        if (caCert is not null)
        {
            sb.Append("<h4 style=\"color:#e6a23c\">② CA 证书临近到期</h4>");
            sb.Append($"<p style=\"color:#e6a23c;font-weight:600\">{Escape(caCert)}</p>");
        }
        if (crl is not null)
        {
            sb.Append("<h4 style=\"color:#f56c6c\">③ CRL 状态告警</h4>");
            sb.Append($"<p style=\"color:#f56c6c;font-weight:600\">{Escape(crl)}</p>");
        }
        if (pending > 0)
        {
            sb.Append($"<h4>④ 待处理请求积压（{pending} 条）</h4>");
            sb.Append("<p>请登录 CA-Mgr 在“请求处理”页面审批。</p>");
        }
        if (notes.Count > 0)
        {
            sb.Append("<h4>备注</h4><ul>");
            foreach (var n in notes) sb.Append($"<li style=\"color:#888\">{Escape(n)}</li>");
            sb.Append("</ul>");
        }
        sb.Append("<p style=\"color:#aaa;font-size:12px;margin-top:18px\">本邮件由 CA-Manager 自动发送</p></div>");
        return sb.ToString();
    }

    private static string BuildRequesterHtml(string email, List<ExpiringRow> rows, int days)
    {
        var sb = new StringBuilder();
        sb.Append("<div style=\"font-family:Segoe UI,Microsoft YaHei,Arial,sans-serif;max-width:800px\">");
        sb.Append($"<h3 style=\"color:#2563eb\">您的证书即将到期</h3>");
        sb.Append($"<p>{Escape(email)}，您好：</p>");
        sb.Append($"<p>以下是您名下 {days} 天内到期的证书，请及时安排续期：</p>");
        sb.Append(Table(rows));
        sb.Append("<p style=\"color:#aaa;font-size:12px;margin-top:18px\">本邮件由 CA-Manager 自动发送，请勿直接回复</p></div>");
        return sb.ToString();
    }
}

/// <summary>Daily scheduler: wakes every minute, fires the scan once per day at notify.dailyAt.</summary>
public sealed class NotifyBackgroundService(
    SettingsService settings,
    NotifyService notify,
    ILogger<NotifyBackgroundService> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // wait for the app to finish starting up before the first check
        await Task.Delay(TimeSpan.FromSeconds(20), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var all = await settings.GetAllAsync();
                var dailyAt = all.TryGetValue("notify.dailyAt", out var t) && TimeSpan.TryParse(t, out var ts) ? ts : new TimeSpan(8, 0, 0);
                var now = DateTime.Now;
                var today = now.Date.ToString("yyyy-MM-dd");
                var lastRun = all.TryGetValue("notify.lastRun", out var lr) ? lr : "";
                var firedToday = lastRun.StartsWith(today);

                if (!firedToday && now.TimeOfDay >= dailyAt)
                {
                    log.LogInformation("Daily notify scan starting");
                    await notify.RunAsync("system", "");
                }
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Notify scheduler iteration failed");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
        }
    }
}
