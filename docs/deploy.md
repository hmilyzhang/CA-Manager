# CA-Manager 部署手册

## 前置条件

| 项 | 要求 |
|----|------|
| 运行位置 | AD CS 服务器本机（COM 本地调用；本系统不支持远程管理其他 CA） |
| 操作系统 | Windows Server 2016+（已在 Server 2025 验证） |
| CA 角色 | 企业版或独立版 AD CS |
| 构建环境 | Node.js 20+ 与 .NET 10 SDK（仅在构建机需要；服务器无需） |
| 服务账户 | 默认 LocalSystem（本机即具备 CA 管理权限）。如需专用账户，将其加入本地 `CA Administrators` / `Cert Publishers` 并以该账户安装服务 |
| 端口 | 默认 8443（可自定义），防火墙需放行 |

## 构建与安装

```powershell
# 构建机：发布（产物 publish\ 目录，可整体拷贝到 CA 服务器）
powershell -File scripts\publish.ps1

# CA 服务器（管理员 PowerShell）：
powershell -File scripts\install-service.ps1
```

安装脚本自动完成：
1. 尝试向本机 CA 申请 WebServer 模板 HTTPS 证书（CN=主机 FQDN）并绑定端口；失败则回退 HTTP 并提示
2. 写入 `publish\appsettings.Production.json` 监听地址
3. `sc create CA-Manager`（LocalSystem、开机自启、崩溃自动重启）
4. 启动服务并打印访问地址

## 首次登录

1. 打开 `https://<主机名>:8443`（回退 HTTP 时为 `http://`）
2. 初始账号 `admin`，初始密码在 `publish\initial-admin-password.txt`
3. 首次登录强制修改密码，改密后此文件即可删除

## 防火墙

```powershell
New-NetFirewallRule -DisplayName "CA-Manager" -Direction Inbound -Protocol TCP -LocalPort 8443 -RemoteAddress Intranet -Action Allow
```

## 生产环境 HTTPS（若自动申请失败）

手动用本 CA 签发一张服务器证书后执行：

```powershell
$thumb = "<证书SHA1指纹>"
netsh http add sslcert ipport=0.0.0.0:8443 certhash=$thumb appid="{4d8a5f2e-6b3c-4a9e-9f2e-ca7mgr000001}" certstorename=MY
# 然后编辑 publish\appsettings.Production.json 的 Kestrel Endpoints 为 https://+:8443
Restart-Service CA-Manager
```

## 升级 / 回滚 / 卸载

```powershell
powershell -File scripts\update.ps1       # 升级（保留 publish\data 数据）
powershell -File scripts\uninstall-service.ps1   # 卸载服务（数据保留在 publish\data）
```

## 数据与备份

- `publish\data\camgr.db`：Web 用户、审计日志、系统设置——纳入常规备份即可
- 审计日志仅追加；CA 数据本身仍由 AD CS 管理，本系统不复制证书库

## 常见问题

| 现象 | 原因与处理 |
|------|-----------|
| 颁发/吊销报 `Access is denied 0x80070005` | 服务未以 LocalSystem 运行，或当前 Web 用户角色不足；检查服务账户是否在 CA Administrators |
| 列表为空且日志出现 `too many active sessions 0x8009400F` | CA 数据库会话耗尽（旧版本泄漏已修复）；重启 CA-Manager 服务并升级到最新版本 |
| CA 显示不可达 | 检查 CertSvc 服务是否运行；本机 COM 依赖 RPC |
| HTTPS 证书申请失败 | 多因 WebServer 模板权限；按上文手动配置 |
