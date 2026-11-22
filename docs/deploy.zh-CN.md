# CA-Manager 部署手册

> English | [简体中文](deploy.md)

## 前置条件

| 项 | 要求 |
|----|------|
| 运行位置 | AD CS 服务器本机（COM 本地调用；本系统不支持远程管理其他 CA） |
| 操作系统 | Windows Server 2016+（已在 Server 2025 验证） |
| CA 角色 | 企业版或独立版 AD CS |
| 构建环境 | Node.js 20+ 与 .NET 10 SDK（仅在构建机需要；服务器无需） |
| 服务账户 | 默认 LocalSystem（本机即具备 CA 管理权限）。如需专用账户，将其加入本地 `CA Administrators` / `Cert Publishers` 并以该账户安装服务 |
| 端口 | 默认 HTTP 8442（可自定义），防火墙需放行 |

## 构建与安装

```powershell
# 构建机：发布（产物 publish\ 目录，可整体拷贝到 CA 服务器）
powershell -File scripts\publish.ps1

# CA 服务器（管理员 PowerShell）：
powershell -File scripts\install-service.ps1
```

安装脚本自动完成：
1. 写入 `Listeners` 监听配置（默认 HTTP 8442）到 `publish\appsettings.Production.json`
2. `sc create CA-Manager`（LocalSystem、开机自启、崩溃自动重启）
3. 启动服务并打印访问地址

## 首次登录

1. 打开 `http://<主机名>:8442`（或所选端口）
2. 初始账号 `admin`，初始密码在 `publish\initial-admin-password.txt`
3. 首次登录强制修改密码，改密后此文件即可删除
4. 随时可到 **系统设置** 页切换 HTTPS（见下方"监听配置"）

## 监听配置（HTTP / HTTPS）

服务读取 Kestrel 的 `Listeners` 配置节，安装脚本将初始值写入 `publish\appsettings.Production.json`：

```json
"Listeners": { "Mode": "https", "HttpPort": 8442, "HttpsPort": 8443, "Thumbprint": "<LocalMachine\\My 中证书的 SHA1 指纹>" }
```

- `Mode`：`http`（安装后默认）/ `https` / `both`
- HTTPS 证书启动时**按指纹从本机 `LocalMachine\My` 证书库加载**；证书缺失/不可读时自动回退 HTTP，不会崩溃循环
- 推荐：在 Web 界面 **系统设置** 页直接修改（模式单选 + 端口 + 证书选择）—— 保存后自动应用配置并重启服务，无需远程桌面
- 也可以直接改 JSON 后执行 `Restart-Service CA-Manager`

> **不要使用 `netsh http add sslcert`** —— Kestrel 不走 http.sys 的 SSL 绑定，`netsh sslcert` 在这里无效（且 appid 含非十六进制字符时会报 "The parameter is incorrect"）。

## 防火墙

```powershell
New-NetFirewallRule -DisplayName "CA-Manager" -Direction Inbound -Protocol TCP -LocalPort 8442,8443 -RemoteAddress Intranet -Action Allow
```

（只需放行 `Listeners` 中实际配置的端口。）

## 生产环境 HTTPS（若安装时未配置）

手动用本 CA 签发一张服务器证书（导入 `LocalMachine\My`），然后通过 **系统设置** 页或 `appsettings.Production.json` 填入指纹并重启服务：

```powershell
# 示例：申请 WebServer 模板证书并导入
certreq -submit -attrib "CertificateTemplate:WebServer" request.req cert.cer
certreq -accept cert.cer        # 证书进入 LocalMachine\My
Restart-Service CA-Manager      # 在系统设置页选中证书 / 编辑 Listeners 之后
```

## 升级 / 回滚 / 卸载

```powershell
powershell -File scripts\update.ps1       # 升级（保留 publish\data 数据）
powershell -File scripts\uninstall-service.ps1   # 卸载服务（数据保留在 publish\data）
```

## 数据与备份

- `publish\data\camgr.db`：Web 用户、审计日志、系统设置——纳入常规备份即可
- 审计日志仅追加；CA 数据本身仍由 AD CS 管理，本系统不复制证书库

## Stand-alone CA（独立 CA）注意事项

独立 CA 没有模板概念，且默认策略是"申请一律挂起等待管理员颁发"：

- **安装时的 HTTPS 证书**：`certreq -submit` 会返回"待处理"，安装脚本回退 HTTP。处理办法：在 CA-Manager **请求处理**页对该请求点**颁发**，然后回到服务器执行 `certreq -retrieve <请求ID> cert.cer` 与 `certreq -accept cert.cer` 完成安装，再按上文切换 HTTPS 绑定。
- **SAN**：独立 CA 默认忽略申请属性里的 SAN。需在 CA 服务器（管理员）启用标志：`certutil -setreg policy\EditFlags +EDITF_ATTRIBUTESUBJECTALTNAME2` 并重启 CertSvc，否则签出的证书不含 SAN。
- **模板**：模板下拉显示"无需选择模板"——独立 CA 会忽略 CertificateTemplate 属性。
- **审批**：默认 pending-first 策略下，所有提交都在 CA 队列等待；在**请求处理**页点"颁发"即可（普通用户提交在此之外仍有应用内审批）。

## 常见问题

| 现象 | 原因与处理 |
|------|-----------|
| 颁发/吊销报 `Access is denied 0x80070005` | 服务未以 LocalSystem 运行，或当前 Web 用户角色不足；检查服务账户是否在 CA Administrators |
| 列表为空且日志出现 `too many active sessions 0x8009400F` | CA 数据库会话耗尽（旧版本泄漏已修复）；重启 CA-Manager 服务并升级到最新版本 |
| CA 显示不可达 | 检查 CertSvc 服务是否运行；本机 COM 依赖 RPC |
| HTTPS 证书申请失败 | 多因 WebServer 模板权限；按上文手动配置 |
