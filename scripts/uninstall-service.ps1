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
    Write-Host "服务已移除。数据目录 publish\data (含用户库与审计日志) 保留。" -ForegroundColor Green
} else {
    Write-Host "服务不存在"
}
