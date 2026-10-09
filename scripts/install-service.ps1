# CA-Manager Windows service installation script
# Usage (admin PowerShell):
#   powershell -File install-service.ps1              # HTTP on 8442 (configure HTTPS in the web UI later)
#   powershell -File install-service.ps1 -Port 9000   # custom HTTP port
param(
    [int]$Port = 8442
)

$ErrorActionPreference = 'Stop'
$svcName = 'CA-Manager'
$root = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $root 'publish'
$exe = Join-Path $publishDir 'CaMgr.Api.exe'
$dataDir = Join-Path $publishDir 'data'

if (-not (Test-Path $exe)) { throw "Not found: $exe - run publish first" }
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated PowerShell"
}

# stop + remove existing service (any naming)
$existing = Get-Service -Name $svcName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping and removing existing service $svcName..." -ForegroundColor Yellow
    if ($existing.Status -ne 'Stopped') { Stop-Service $svcName -Force -ErrorAction SilentlyContinue }
    sc.exe delete $svcName | Out-Null
}
$legacy = Get-Service -Name 'CA-Mgr' -ErrorAction SilentlyContinue
if ($legacy) {
    Write-Host "Removing legacy-named service CA-Mgr..." -ForegroundColor Yellow
    Stop-Service 'CA-Mgr' -Force -ErrorAction SilentlyContinue
    sc.exe delete 'CA-Mgr' | Out-Null
}
Start-Sleep -Seconds 2

# kill any lingering app processes holding ports (zombie protection)
$zombies = Get-Process -Name 'CaMgr.Api' -ErrorAction SilentlyContinue
if ($zombies) {
    Write-Host "Killing lingering CaMgr.Api process(es)..." -ForegroundColor Yellow
    $zombies | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
}

# listener configuration (Kestrel reads the Listeners section; TLS is configured in the web UI)
$settingsPath = Join-Path $publishDir 'appsettings.Production.json'
@{
    Listeners = @{
        Mode     = 'http'
        HttpPort = $Port
    }
} | ConvertTo-Json -Depth 5 | Set-Content -Path $settingsPath -Encoding UTF8

Write-Host "Creating service $svcName (binPath=$exe)..." -ForegroundColor Cyan
sc.exe create $svcName binPath= "`"$exe`"" start= auto DisplayName= "CA-Manager Web Console" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "sc create failed" }
# sc.exe mangles non-ASCII - set the description via registry
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName" -Name DisplayName -Value "CA-Manager Certificate Services Management"
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$svcName" -Name Description -Value "CA-Manager - AD CS web management console (certificates / revocation / requests / CRL / templates / PGP)"
sc.exe failure $svcName reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null

sc.exe start $svcName | Out-Null
Start-Sleep -Seconds 4
$svc = Get-Service -Name $svcName
Write-Host "`nService status: $($svc.Status)" -ForegroundColor Green
Write-Host "Web UI:         http://<hostname>:$Port" -ForegroundColor Green
Write-Host "Initial admin:  account=admin, password in $publishDir\initial-admin-password.txt (change forced at first login)"
Write-Host "HTTPS:          configure in the web UI (System settings) - pick a local server certificate"
