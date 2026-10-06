# CA-Mgr 更新脚本：重新构建前端/后端并重启服务
# 用法: 以管理员身份运行 powershell -File update.ps1
$ErrorActionPreference = 'Stop'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "请以管理员身份运行本脚本"
}

$root = Split-Path -Parent $PSScriptRoot
Write-Host "停止服务..." -ForegroundColor Cyan
Stop-Service CA-Mgr -Force -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

Write-Host "构建前端..." -ForegroundColor Cyan
Push-Location (Join-Path $root 'src\CaMgr.Web')
npm run build
if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
Pop-Location

Write-Host "发布后端..." -ForegroundColor Cyan
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet publish (Join-Path $root 'src\CaMgr.Api\CaMgr.Api.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $root 'publish')
if ($LASTEXITCODE -ne 0) { throw "发布失败" }

Write-Host "启动服务..." -ForegroundColor Cyan
Start-Service CA-Mgr
Start-Sleep -Seconds 3
(Get-Service CA-Mgr).Status
