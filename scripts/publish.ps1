# CA-Mgr 发布脚本：构建前端 + 后端 self-contained 发布
# 用法: powershell -File publish.ps1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'src\CaMgr.Web'
$api = Join-Path $root 'src\CaMgr.Api'
$out = Join-Path $root 'publish'

Write-Host "=== 1/3 构建前端 ===" -ForegroundColor Cyan
Push-Location $web
npm install --no-audit --no-fund 2>&1 | Out-Null
npm run build
if ($LASTEXITCODE -ne 0) { throw "前端构建失败" }
Pop-Location

Write-Host "=== 2/3 发布后端 (self-contained, win-x64) ===" -ForegroundColor Cyan
$dotnet = Join-Path $root '.dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet publish (Join-Path $api 'CaMgr.Api.csproj') `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=false `
    -o $out
if ($LASTEXITCODE -ne 0) { throw "后端发布失败" }

Write-Host "=== 3/3 完成 ===" -ForegroundColor Cyan
Write-Host "发布目录: $out"
Write-Host "下一步: 以管理员运行 scripts\install-service.ps1 注册 Windows 服务"
