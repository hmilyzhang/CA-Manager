# CA-Mgr 服务卸载
# 用法: 以管理员身份运行
$ErrorActionPreference = 'Stop'
$svcName = 'CA-Manager'
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "请以管理员身份运行"
}
$svc = Get-Service -Name $svcName -ErrorAction SilentlyContinue
if ($svc) {
    if ($svc.Status -ne 'Stopped') { Stop-Service -Name $svcName -Force }
    sc.exe delete $svcName
    Write-Host "Service removed. Data (publish\data) is kept." -ForegroundColor Green
} else {
    Write-Host "Service not found"
}
