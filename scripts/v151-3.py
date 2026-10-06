# -*- coding: utf-8 -*-
"""v1.5.1 phase 3: dashboard viewer scoping + frontend roles/menus."""
import io

def patch(path, pairs):
    s = io.open(path, encoding='utf-8').read()
    for old, new in pairs:
        if old not in s:
            raise SystemExit(f"MISS in {path}: {old[:80]!r}")
        s = s.replace(old, new, 1)
    io.open(path, 'w', encoding='utf-8').write(s)
    print('patched', path)

# ---- DashboardController: viewer-scoped numbers ----
patch(r'D:\CA-Mgr\src\CaMgr.Api\Controllers\DashboardController.cs', [
    ("""public sealed class DashboardController(
    CertificateService certs,
    CaAdminService admin,
    CaContext ca,
    IDbContextFactory<Data.AppDbContext> dbf) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Overview()
    {
        await using var db = dbf.CreateDbContext();
        bool alive = await admin.TryPingAsync();
        var issued = alive ? await certs.ListAsync(CertStatusFilter.Issued, limit: 2000) : null;
        var pending = alive ? await certs.ListAsync(CertStatusFilter.Pending, limit: 500) : null;
        var revoked = alive ? await certs.ListAsync(CertStatusFilter.Revoked, limit: 2000) : null;""",
     """public sealed class DashboardController(
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
        }"""),
    ("""                issued = issued?.TotalFetched ?? 0,
                pending = pending?.TotalFetched ?? 0,
                revoked = revoked?.TotalFetched ?? 0,""",
     """                issued = issued?.TotalFetched ?? 0,
                pending = pendingCount,
                revoked = revoked?.TotalFetched ?? 0,"""),
    ("""        var recentAudit = db.AuditLogs.OrderByDescending(a => a.Id).Take(5)""",
     """        var recentAudit = db.AuditLogs.OrderByDescending(a => a.Id)
            .Where(a => !isViewer || a.Username == caller)
            .Take(5)"""),
])

# ---- store.js: role helpers ----
patch(r'D:\CA-Mgr\src\CaMgr.Web\src\store.js', [
    ("""export function roleAtLeast(role, min) {
  const order = { Viewer: 0, Operator: 1, Admin: 2 }
  return (order[role] ?? -1) >= (order[min] ?? 99)
}""",
     """// action hierarchy: Auditor has read-all rights but performs no actions
export function roleAtLeast(role, min) {
  if (min === 'Operator') return role === 'Operator' || role === 'Admin'
  if (min === 'Admin') return role === 'Admin'
  return false
}

// global read access: Admin and Auditor
export function canSeeAll(role) {
  return role === 'Admin' || role === 'Auditor'
}"""),
])

# ---- MainLayout: menus ----
patch(r'D:\CA-Mgr\src\CaMgr.Web\src\layout\MainLayout.vue', [
    # viewer gets New Request + PGP (remove operator gating)
    ("""        <el-menu-item index="/new-request" v-if="roleAtLeast(role, 'Operator')"><el-icon><Upload /></el-icon>{{ $t('nav.newRequest') }}</el-menu-item>""",
     """        <el-menu-item index="/new-request" v-if="role !== 'Auditor'"><el-icon><Upload /></el-icon>{{ $t('nav.newRequest') }}</el-menu-item>"""),
    ("""        <el-menu-item index="/pgp" v-if="roleAtLeast(role, 'Operator')"><el-icon><Key /></el-icon>{{ $t('nav.pgp') }}</el-menu-item>""",
     """        <el-menu-item index="/pgp" v-if="role !== 'Auditor'"><el-icon><Key /></el-icon>{{ $t('nav.pgp') }}</el-menu-item>"""),
    # requests queue: global view — operator/auditor/admin
    ("""        <el-menu-item index="/requests"><el-icon><List /></el-icon>{{ $t('nav.requests') }}</el-menu-item>""",
     """        <el-menu-item index="/requests" v-if="roleAtLeast(role, 'Operator') || role === 'Auditor'"><el-icon><List /></el-icon>{{ $t('nav.requests') }}</el-menu-item>"""),
    # audit: admin or auditor
    ("""        <el-menu-item index="/audit" v-if="roleAtLeast(role, 'Admin')"><el-icon><Document /></el-icon>{{ $t('nav.audit') }}</el-menu-item>""",
     """        <el-menu-item index="/audit" v-if="roleAtLeast(role, 'Admin') || role === 'Auditor'"><el-icon><Document /></el-icon>{{ $t('nav.audit') }}</el-menu-item>"""),
])

# ---- Users.vue: Auditor role option ----
patch(r'D:\CA-Mgr\src\CaMgr.Web\src\views\Users.vue', [
    ("""            <el-option value="Viewer" :label="$t('role.Viewer')" />
            <el-option value="Operator" :label="$t('role.Operator')" />
            <el-option value="Admin" :label="$t('role.Admin')" />
          </el-select>
        </template>
      </el-table-column>""",
     """            <el-option value="Viewer" :label="$t('role.Viewer')" />
            <el-option value="Operator" :label="$t('role.Operator')" />
            <el-option value="Admin" :label="$t('role.Admin')" />
            <el-option value="Auditor" :label="$t('role.Auditor')" />
          </el-select>
        </template>
      </el-table-column>"""),
    ("""          <el-select v-model="createForm.role" style="width: 100%">
            <el-option value="Viewer" :label="$t('role.Viewer')" />
            <el-option value="Operator" :label="$t('role.Operator')" />
            <el-option value="Admin" :label="$t('role.Admin')" />
          </el-select>""",
     """          <el-select v-model="createForm.role" style="width: 100%">
            <el-option value="Viewer" :label="$t('role.Viewer')" />
            <el-option value="Operator" :label="$t('role.Operator')" />
            <el-option value="Admin" :label="$t('role.Admin')" />
            <el-option value="Auditor" :label="$t('role.Auditor')" />
          </el-select>"""),
])

# ---- NewRequest.vue: auditor also sees no submit (read-only watcher) ----
patch(r'D:\CA-Mgr\src\CaMgr.Web\src\views\NewRequest.vue', [
    ("""const isViewer = computed(() => !roleAtLeast(store.user?.role, 'Operator'))""",
     """const isViewer = computed(() => store.user?.role === 'Viewer')"""),
])

# ---- i18n: role labels + auditor ----
p = r'D:\CA-Mgr\src\CaMgr.Web\src\i18n.js'
s = io.open(p, encoding='utf-8').read()
s = s.replace("role: { Viewer: '只读', Operator: '操作员', Admin: '管理员' },",
              "role: { Viewer: '普通用户', Operator: '操作员', Admin: '管理员', Auditor: '审计员' },")
s = s.replace("role: { Viewer: 'Viewer', Operator: 'Operator', Admin: 'Administrator' },",
              "role: { Viewer: 'User', Operator: 'Operator', Admin: 'Administrator', Auditor: 'Auditor' },")
io.open(p, 'w', encoding='utf-8').write(s)
print('i18n ok')

print('PHASE 3 DONE')
