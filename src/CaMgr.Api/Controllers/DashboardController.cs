using CaMgr.Api.CaInterop;
using CaMgr.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CaMgr.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(
    CertificateService certs,
    CaAdminService admin,
    CaContext ca,
    CaMgr.Api.Services.ApprovalService approvals,
    IDbContextFactory<Data.AppDbContext> dbf) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Overview()
    {
        await using var db = dbf.CreateDbContext();
        bool alive = await admin.TryPingAsync();

        // viewers see their own footprint instead of global CA statistics
        System.Security.Claims.Claim? roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role);
        var isViewer = Enum.TryParse<Data.AppRole>(roleClaim?.Value, out var myRole) && myRole == Data.AppRole.Viewer;
        System.Collections.Generic.ISet<int>? scope = isViewer ? await approvals.GetOwnedRequestIdsAsync(User.Identity?.Name ?? "") : null;
        var caller = User.Identity?.Name ?? "";

        var issued = alive ? await certs.ListAsync(CertStatusFilter.Issued, limit: 2000, restrictRequestIds: scope) : null;
        var pending = alive && !isViewer ? await certs.ListAsync(CertStatusFilter.Pending, limit: 500) : null;
        var revoked = alive ? await certs.ListAsync(CertStatusFilter.Revoked, limit: 2000, restrictRequestIds: scope) : null;

        int pendingCount = pending?.TotalFetched ?? 0;
        if (isViewer)
        {
            var myApprovals = await approvals.ListAsync("pending", caller, canSeeAll: false);
            pendingCount = myApprovals.Count;
        }

        int now7 = 0;
        int exp30 = 0;
        var byDay = new Dictionary<DateTime, int>();
        var cutoff7 = DateTime.Now.AddDays(-7);
        var cutoff30 = DateTime.Now.AddDays(30);

        if (issued is not null)
        {
            foreach (var c in issued.Items)
            {
                if (c.SubmittedAt >= cutoff7) now7++;
                if (c.NotAfter.HasValue && c.NotAfter.Value >= DateTime.Now && c.NotAfter.Value <= cutoff30) exp30++;
                var day = (c.SubmittedAt ?? DateTime.Now).Date;
                byDay.TryGetValue(day, out var n);
                byDay[day] = n + 1;
            }
        }

        // CA certificate expiry
        string? caCertExpiry = null;
        try
        {
            var cert = CryptoParse.ParseCert(await admin.GetCaPropertyBytesAsync(CaConst.CR_PROP_CASIGCERT, 0));
            caCertExpiry = cert?.NotAfter.ToString("yyyy-MM-dd HH:mm:ss");
        }
        catch { }

        var recentAudit = db.AuditLogs.OrderByDescending(a => a.Id)
            .Where(a => !isViewer || a.Username == caller)
            .Take(5)
            .Select(a => new { a.At, a.Username, a.Action, a.ObjectId, a.Success }).ToList();

        return Ok(new
        {
            alive,
            totals = new
            {
                issued = issued?.TotalFetched ?? 0,
                pending = pendingCount,
                revoked = revoked?.TotalFetched ?? 0,
                last7Days = now7,
                expiring30 = exp30,
            },
            caCertificateExpiry = caCertExpiry,
            trend = byDay.OrderByDescending(k => k.Key).Take(30).Select(k => new { date = k.Key.ToString("yyyy-MM-dd"), count = k.Value }),
            recentAudit,
        });
    }
}
