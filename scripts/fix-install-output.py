# -*- coding: utf-8 -*-
"""Install/uninstall scripts: English console output (fixes mojibake) + correct password path."""
import io, codecs

def load(p):
    return io.open(p, encoding='utf-8-sig').read()

def save(p, s):
    io.open(p, 'w', encoding='utf-8-sig').write(s)
    print('patched', p)

# ---- install-service.ps1 ----
p = r'D:\CA-Mgr\scripts\install-service.ps1'
s = load(p)
pairs = [
    ('Write-Host "停止并移除已有服务 $svcName..." -ForegroundColor Yellow',
     'Write-Host "Stopping and removing existing service $svcName..." -ForegroundColor Yellow'),
    ('Write-Host "移除旧命名服务 CA-Mgr..." -ForegroundColor Yellow',
     'Write-Host "Removing legacy-named service CA-Mgr..." -ForegroundColor Yellow'),
    ('Write-Host "尝试为本机申请 HTTPS 证书..." -ForegroundColor Cyan',
     'Write-Host "Requesting HTTPS certificate from the local CA..." -ForegroundColor Cyan'),
    ('Write-Host "HTTPS 证书已颁发并安装: $httpsCertThumb" -ForegroundColor Green',
     'Write-Host "HTTPS certificate issued and installed: $httpsCertThumb" -ForegroundColor Green'),
    ('Write-Warning "HTTPS 证书申请失败($_)：回退为 HTTP。生产环境请手动配置证书后重装。"',
     'Write-Warning "HTTPS enrollment failed ($_) - falling back to HTTP. See docs/deploy.md to configure HTTPS manually."'),
    ('Write-Host "创建服务 $svcName (binPath=$exe)..." -ForegroundColor Cyan',
     'Write-Host "Creating service $svcName (binPath=$exe)..." -ForegroundColor Cyan'),
    ('Write-Host "`n服务状态: $($svc.Status)" -ForegroundColor Green',
     'Write-Host "`nService status: $($svc.Status)" -ForegroundColor Green'),
    ('Write-Host "访问地址: $scheme`://$fqdn`:$Port (或 https://localhost:$Port)" -ForegroundColor Green',
     'Write-Host "Web UI:        $scheme`://$fqdn`:$Port  (or $scheme`://localhost:$Port)" -ForegroundColor Green'),
    ('Write-Host "初始账号见: $publishDir\\data\\initial-admin-password.txt (首次登录强制改密)"',
     'Write-Host "Initial admin: account=admin, password in $publishDir\\initial-admin-password.txt (change forced at first login)"'),
    ('# CA-Manager Windows 服务安装脚本', '# CA-Manager Windows service installation script'),
]
for old, new in pairs:
    if old not in s:
        print('WARN missing:', old[:70])
        continue
    s = s.replace(old, new)
save(p, s)

# ---- uninstall-service.ps1 ----
p = r'D:\CA-Mgr\scripts\uninstall-service.ps1'
s = load(p)
s = s.replace('Write-Host "服务已移除。数据目录 publish\\data (含用户库与审计日志) 保留。" -ForegroundColor Green',
              'Write-Host "Service removed. Data (publish\\data) is kept." -ForegroundColor Green')
s = s.replace('Write-Host "服务不存在"', 'Write-Host "Service not found"')
save(p, s)
print('DONE')
