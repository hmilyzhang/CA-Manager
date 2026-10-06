# -*- coding: utf-8 -*-
"""v1.5.1: Auditor role + viewer self-scoping + menu fixes."""
import io, re

def patch(path, pairs, enc='utf-8'):
    s = io.open(path, encoding=enc).read()
    for old, new in pairs:
        if old not in s:
            raise SystemExit(f"MISS in {path}: {old[:80]!r}")
        s = s.replace(old, new, 1)
    io.open(path, 'w', encoding=enc).write(s)
    print('patched', path)

def patch_all(path, pairs, enc='utf-8'):
    s = io.open(path, encoding=enc).read()
    for old, new in pairs:
        if old not in s:
            raise SystemExit(f"MISS in {path}: {old[:80]!r}")
        s = s.replace(old, new)
    io.open(path, 'w', encoding=enc).write(s)
    print('patched', path)

# ============ 1) AppRole + multi-role RequireRole ============
patch(r'D:\CA-Mgr\src\CaMgr.Api\Data\AppDbContext.cs', [
    ("""public enum AppRole
{
    Viewer = 0,    // 只读：查询/下载
    Operator = 1,  // 操作员：+ 颁发/拒绝/吊销/提交/CRL发布
    Admin = 2,     // 管理员：+ 配置/模板/用户管理/审计
}""",
"""public enum AppRole
{
    Viewer = 0,    // 只读+申请：仅本人提交的内容可查；可提交证书申请（需审批）、生成 PGP 密钥
    Operator = 1,  // 操作员：全局可读 + 颁发/拒绝/吊销/CRL发布/审批
    Admin = 2,     // 管理员：全局可读 + 配置/模板/用户管理/审计
    Auditor = 3,   // 审计员：全局可读（含审计日志），无任何操作权限
}""")])

# RequireRoleAttribute: allow-list of roles
patch(r'D:\CA-Mgr\src\CaMgr.Api\Controllers\CertificatesController.cs', [
    ("""[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRoleAttribute(AppRole min) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext ctx)
    {
        var roleStr = ctx.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (!Enum.TryParse<AppRole>(roleStr, out var role) || role < min)
            ctx.Result = new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
    }
}""",
"""[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireRoleAttribute(params AppRole[] allowed) : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext ctx)
    {
        var roleStr = ctx.HttpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value;
        if (!Enum.TryParse<AppRole>(roleStr, out var role) || !allowed.Contains(role))
            ctx.Result = new JsonResult(new { error = "权限不足" }) { StatusCode = 403 };
    }
}"""),
])

# swap every "RequireRole(AppRole.X)" single-role usage to allow-lists
# Operator gates -> Operator|Admin ; Admin gates -> Admin|Auditor (audit) or Admin (user mgmt, admin-only configs)
import glob
for f in glob.glob(r'D:\CA-Mgr\src\CaMgr.Api\Controllers\*.cs'):
    s = io.open(f, encoding='utf-8').read()
    orig = s
    s = s.replace('[RequireRole(AppRole.Operator)]', '[RequireRole(AppRole.Operator, AppRole.Admin)]')
    if f.endswith('AuditController.cs'):
        s = s.replace('[RequireRole(AppRole.Admin)]', '[RequireRole(AppRole.Admin, AppRole.Auditor)]')
    if s != orig:
        io.open(f, 'w', encoding='utf-8').write(s)
        print('roles swapped:', f)

print('PHASE 1 DONE')
