# CA-Manager Windows 服务安装脚本
# 用法: 以管理员身份运行 powershell -File install-service.ps1 [-Port 8443] [-UseHttp]
#
# 默认以 LocalSystem 运行（本机 CA 管理权限），监听 HTTPS 端口。
# HTTPS 证书: 优先向本机 CA 申请 (WebServer 模板, CN=主机FQDN)；失败则回退 HTTP。
param(
    [int]$Port = 8443,
    [switch]$UseHttp
)

$ErrorActionPreference = 'Stop'
$svcName = 'CA-Manager'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish'
$exe = Join-Path $publishDir 'CaMgr.Api.exe'
$dataDir = Join-Path $publishDir 'data'

if (-not (Test-Path $exe)) { throw "未找到 $exe，请先运行 publish.ps1" }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "请以管理员身份运行本脚本"
}

# 已存在则先删除（含旧版 CA-Mgr 服务名）
$existing = Get-Service -Name $svcName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "停止并移除已有服务 $svcName..." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $svcName -Force }
    sc.exe delete $svcName | Out-Null
    Start-Sleep -Seconds 2
}
$legacy = Get-Service -Name 'CA-Mgr' -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Host "移除旧命名服务 CA-Mgr..." -ForegroundColor Yellow
    if ($legacy.Status -ne 'Stopped') { Stop-Service -Name 'CA-Mgr' -Force -ErrorAction SilentlyContinue }
    sc.exe delete 'CA-Mgr' | Out-Null
    Start-Sleep -Seconds 2
}

$useHttp = $UseHttp
$httpsCertThumb = $null

if (-not $useHttp) {
    Write-Host "尝试为本机申请 HTTPS 证书..." -ForegroundColor Cyan
    $fqdn = [System.Net.Dns]::GetHostByName($env:COMPUTERNAME).HostName
    $inf = Join-Path $env:TEMP "camgr-https.inf"
    $csr = Join-Path $env:TEMP "camgr-https.csr"
    $cert = Join-Path $env:TEMP "camgr-https.cer"
    @"
[NewRequest]
Subject = "CN=$fqdn"
KeySpec = 2
KeyLength = 2048
MachineKeySet = TRUE
ProviderName = "Microsoft Software Key Storage Provider"
RequestType = PKCS10
[Extensions]
2.5.29.17 = "{text}dns=$fqdn&dns=$env:COMPUTERNAME&ipaddress=0.0.0.0"
[RequestAttributes]
CertificateTemplate = WebServer
"@ | Set-Content -Path $inf -Encoding ASCII

    try {
        certreq -new $inf $csr 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "certreq -new 失败" }
        $config = (certutil -getconfig | Select-Object -First 1) -replace '^Config String: "|"$',''
        certreq -submit -config $config $csr $cert 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0 -or -not (Test-Path $cert)) { throw "certreq -submit 失败" }
        certreq -accept -machine $cert 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "certreq -accept 失败" }
        $httpsCertThumb = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($cert)).Thumbprint
        Write-Host "HTTPS 证书已颁发并安装: $httpsCertThumb" -ForegroundColor Green
        # 绑定端口
        netsh http add sslcert hostnameport="${fqdn}:$Port" certhash=$httpsCertThumb appid=`{4d8a5f2e-6b3c-4a9e-9f2e-ca7mgr000001`} certstorename=MY 2>$null
        if ($LASTEXITCODE -ne 0) {
            netsh http add sslcert ipport=0.0.0.0:$Port certhash=$httpsCertThumb appid=`{4d8a5f2e-6b3c-4a9e-9f2e-ca7mgr000001`} certstorename=MY 2>$null
        }
    } catch {
        Write-Warning "HTTPS 证书申请失败($_)：回退为 HTTP。生产环境请手动配置证书后重装。"
        $useHttp = $true
    }
}

$scheme = if ($useHttp) { 'http' } else { 'https' }
$urls = "$scheme`://+:$Port"

# 写 appsettings 覆盖端口
$settingsPath = Join-Path $publishDir 'appsettings.Production.json'
@{
    Kestrel = @{ Endpoints = @{ Http = @{ Url = $urls } } }
} | ConvertTo-Json -Depth 5 | Set-Content -Path $settingsPath -Encoding UTF8

New-Item -ItemType Directory -Force -Path $dataDir | Out-Null

Write-Host "创建服务 $svcName (binPath=$exe)..." -ForegroundColor Cyan
sc.exe create $svcName binPath= "`"$exe`"" start= auto DisplayName= "CA-Manager Web Console" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc create 失败" }
# sc.exe 写中文会乱码，通过注册表设置中文显示名与描述
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName" -Name DisplayName -Value "CA-Manager 证书服务管理"
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName" -Name Description -Value "CA-Manager - AD CS Web 管理控制台 (证书查询/吊销/请求处理/CRL/模板)"
sc.exe failure $svcName reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null

# 本服务即本机 CA 管理，LocalSystem 默认具有 CA 管理员权限
sc.exe start $svcName | Out-Null
Start-Sleep -Seconds 3
$svc = Get-Service -Name $svcName
Write-Host "`n服务状态: $($svc.Status)" -ForegroundColor Green
Write-Host "访问地址: $scheme`://$fqdn`:$Port (或 https://localhost:$Port)" -ForegroundColor Green
Write-Host "初始账号见: $publishDir\data\initial-admin-password.txt (首次登录强制改密)"
