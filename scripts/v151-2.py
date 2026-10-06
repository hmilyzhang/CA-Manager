# -*- coding: utf-8 -*-
"""v1.5.1 phase 2: viewer self-scoping, auditor read-all."""
import io

def patch(path, pairs):
    s = io.open(path, encoding='utf-8').read()
    for old, new in pairs:
        if old not in s:
            raise SystemExit(f"MISS in {path}: {old[:80]!r}")
        s = s.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8').write(s)
    print('patched', path)

# ---- ApprovalService: owned request ids ----
patch(r'D:\CA-Mgr\src\CaMgr.Api\Services\ApprovalService.cs', [
    ("""    public async Task<List<ApprovalDto>> ListAsync(string? status, string caller, bool isOperator)""",
     """    /// <summary>CA request ids owned by a viewer (via their approved submissions).</summary>
    public async Task<HashSet<int>> GetOwnedRequestIdsAsync(string username)
    {
        await using var ctx = dbf.CreateDbContext();
        var ids = await ctx.ApprovalRequests.AsNoTracking()
            .Where(a => a.Username == username && a.RequestId != null)
            .Select(a => a.RequestId!.Value).ToListAsync();
        return [.. ids];
    }

    public async Task<List<ApprovalDto>> ListAsync(string? status, string caller, bool canSeeAll)"""),
])

# ---- CertificateService: restrictRequestIds param ----
patch(r'D:\CA-Mgr\src\CaMgr.Api\Services\CertificateService.cs', [
    ("""    public async Task<CertificateListResult> ListAsync(
        CertStatusFilter status = CertStatusFilter.All,
        string? keyword = null,
        string? serial = null,
        string? template = null,
        DateTime? from = null, DateTime? to = null,
        int limit = 50,
        int? beforeRequestId = null,
        int expiringDays = 30)
    {""",
     """    public async Task<CertificateListResult> ListAsync(
        CertStatusFilter status = CertStatusFilter.All,
        string? keyword = null,
        string? serial = null,
        string? template = null,
        DateTime? from = null, DateTime? to = null,
        int limit = 50,
        int? beforeRequestId = null,
        int expiringDays = 30,
        ISet<int>? restrictRequestIds = null)
    {"""),
    ("""        Func<CaRow, bool>? post = null;
        if (kw || status == CertStatusFilter.Expiring)
        {""",
     """        Func<CaRow, bool>? post = null;
        if (kw || status == CertStatusFilter.Expiring || restrictRequestIds is not null)
        {"""),
    ("""            post = row =>
            {
                if (kw)
                {""",
     """            post = row =>
            {
                if (restrictRequestIds is not null && !restrictRequestIds.Contains(row.RequestId)) return false;
                if (kw)
                {"""),
])

# ---- CertificatesController: viewer scoping on list/detail/download/export ----
patch(r'D:\CA-Mgr\src\CaMgr.Api\Controllers\CertificatesController.cs', [
    ("""public sealed class CertificatesController(
    CertificateService certs,
    CaDbService db,
    CaAdminService admin,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";""",
     """public sealed class CertificatesController(
    CertificateService certs,
    CaDbService db,
    CaAdminService admin,
    ApprovalService approvals,
    AuditService audit) : Controller
{
    private string Ip => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
    private string User_ => User.Identity?.Name ?? "";
    private AppRole Role =>
        Enum.TryParse<AppRole>(User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value, out var r) ? r : AppRole.Viewer;

    /// <summary>Viewers only see certificates originating from their own approved submissions.</summary>
    private async Task<HashSet<int>?> OwnedScopeAsync()
        => Role == AppRole.Viewer ? await approvals.GetOwnedRequestIdsAsync(User_) : null;"""),
    ("""        if (!Enum.TryParse<CertStatusFilter>(status, true, out var st)) st = CertStatusFilter.All;
        limit = Math.Clamp(limit, 1, 200);
        var result = await certs.ListAsync(st, keyword, serial, template, from?.ToUniversalTime(), to?.ToUniversalTime(), limit, before, expiringDays);
        return Ok(result);""",
     """        if (!Enum.TryParse<CertStatusFilter>(status, true, out var st)) st = CertStatusFilter.All;
        limit = Math.Clamp(limit, 1, 200);
        var scope = await OwnedScopeAsync();
        var result = await certs.ListAsync(st, keyword, serial, template, from?.ToUniversalTime(), to?.ToUniversalTime(), limit, before, expiringDays, scope);
        return Ok(result);"""),
    ("""    public async Task<IActionResult> Detail(int requestId)
    {
        var d = await certs.DetailAsync(requestId);
        return d is null ? NotFound(new { error = "请求不存在" }) : Ok(d);""",
     """    public async Task<IActionResult> Detail(int requestId)
    {
        var scope = await OwnedScopeAsync();
        if (scope is not null && !scope.Contains(requestId)) return Forbid();
        var d = await certs.DetailAsync(requestId);
        return d is null ? NotFound(new { error = "请求不存在" }) : Ok(d);"""),
    ("""    public async Task<IActionResult> Download(int requestId, [FromQuery] string format = "cer")
    {
        var d = await certs.DetailAsync(requestId);""",
     """    public async Task<IActionResult> Download(int requestId, [FromQuery] string format = "cer")
    {
        var scope = await OwnedScopeAsync();
        if (scope is not null && !scope.Contains(requestId)) return Forbid();
        var d = await certs.DetailAsync(requestId);"""),
    ("""        if (!Enum.TryParse<CertStatusFilter>(status, true, out var st)) st = CertStatusFilter.All;
        var result = await certs.ListAsync(st, keyword, serial, template, from?.ToUniversalTime(), to?.ToUniversalTime(), 5000);

        var sb = new StringBuilder();""",
     """        if (!Enum.TryParse<CertStatusFilter>(status, true, out var st)) st = CertStatusFilter.All;
        var scope = await OwnedScopeAsync();
        var result = await certs.ListAsync(st, keyword, serial, template, from?.ToUniversalTime(), to?.ToUniversalTime(), 5000, expiringDays: 30, restrictRequestIds: scope);

        var sb = new StringBuilder();"""),
])

# ---- RequestsController: queue gated to operator/auditor/admin ----
patch(r'D:\CA-Mgr\src\CaMgr.Api\Controllers\RequestsController.cs', [
    ("""    /// <summary>Pending/failed/denied queue.</summary>
    [HttpGet("queue")]""",
     """    /// <summary>Pending/failed/denied queue (global view: operator/auditor/admin).</summary>
    [HttpGet("queue")]
    [RequireRole(AppRole.Operator, AppRole.Admin, AppRole.Auditor)]"""),
])

# ---- ApprovalsController: auditor sees all ----
patch(r'D:\CA-Mgr\src\CaMgr.Api\Controllers\ApprovalsController.cs', [
    ("""    private bool IsOperator =>
        Enum.TryParse<AppRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var role) && role >= AppRole.Operator;""",
     """    private bool IsOperator =>
        Enum.TryParse<AppRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var role) && role >= AppRole.Operator;
    private bool CanSeeAll =>
        Enum.TryParse<AppRole>(User.FindFirst(ClaimTypes.Role)?.Value, out var role) && role >= AppRole.Operator;"""),
])

print('PHASE 2 DONE')
