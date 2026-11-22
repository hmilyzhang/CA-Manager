# CA-Manager — AD CS 证书服务 Web 管理系统

> [English](README.md) | 简体中文

把 Windows AD CS（Active Directory 证书服务）的全部日常运维搬到浏览器：不再需要远程桌面登录 CA 服务器执行 certutil。

## 功能总览

| 模块 | 功能 |
|------|------|
| 仪表盘 | CA 状态/证书统计/近 30 天趋势/最新操作 |
| 证书管理 | 查询（状态/关键字/序列号/时间/模板筛选，**含已过期**）、**真服务端分页**（支持 3000+ 证书）、详情（SAN/EKU/密钥/指纹/链）、下载 CER/PEM、CSV 导出、**吊销**（7 种原因+失效日期+序列号二次确认）、**取消吊销**（certificateHold）、**过期证书批量吊销** |
| 到期提醒 | 30/60/90 天阈值、CA 证书到期告警 |
| 请求处理 | 待处理/已拒绝/失败队列、**颁发/拒绝/重新提交** |
| 证书审批 | 普通用户提交先入审批队列，操作员/管理员批准后才提交 CA；自助密钥加密托管（AES-256-GCM），颁发后一次性下载 PFX |
| 提交申请 | 两种模式：**自助生成**（服务器代生成密钥对，默认 RSA4096，支持 SAN 多域名/IP 混合，颁发后直接下载 PFX）与**粘贴 CSR**（PEM/Base64，私钥留在本机） |
| PGP 密钥 | 生成 OpenPGP 密钥对用于文件加密（默认 ECC Ed25519，有效期 1/2 年，GnuPG 兼容；私钥仅返回一次，服务器不留存）+ 密钥追踪记录（active/revoked/lost/compromised） |
| 资产清单 | 统一资产管理：CA 颁发证书、PGP 密钥、第三方导入证书 —— 负责人/使用人/备注/生命周期状态、一键从 CA 同步、CSV 导出、自动计算到期状态 |
| 证书模板 | 已启用模板列表（含 AD 详情）、管理员启停模板 |
| CA 与 CRL | CA 属性/证书链/CDP/AIA、CRL 周期查看与修改、**手动发布 CRL/Delta**、CRL 下载与内容解析、CA 证书下载（CER/P7B/zip 压缩包） |
| 通知设置 | SMTP 邮件（内网匿名/STARTTLS/SSL）：每日汇总（证书到期/CA 证书/CRL 状态/待处理积压/**清单到期/OCSP 健康**）+ 申请者单独通知 |
| 用户管理 | 本地账号 + AD 域账号（LDAP），四级 RBAC（管理员/操作员/审计员/用户）+ 机器自动化 **API Token** |
| 审计日志 | 全部敏感操作记录（登录/吊销/颁发/配置变更/PGP 生成/清单变更/Token 使用…），查询与 CSV 导出 |
| 系统设置 | 监听配置（HTTP/HTTPS/双栈、端口、证书指纹 —— 保存后自动重启生效）、本机证书库浏览、**OCSP 在线响应器健康监控** |
| API 文档 | 内置双语 REST 接口参考页（`/apidocs`），curl/PHP/Java/PowerShell 示例可直接复制 |

## 机器自动化 REST API

Web 服务器无需浏览器即可申请/续订证书：

1. 管理员为每台机器创建一个 Token（**用户管理** → **API Tokens**）—— `cam_` 前缀、操作员权限、1 年有效、SHA256 哈希存储
2. 携带 `Authorization: Bearer cam_...` 调用：

| 端点 | 用途 |
|------|------|
| `POST /api/automation/certificate` | 申请证书（CN/SAN/密钥算法/模板）—— 颁发即返回 PFX |
| `POST /api/automation/renew` | 按旧证书序列号换发；旧证书保留 |
| `GET /api/automation/status/{requestId}` | 查询申请状态 |

其余全部 `/api` 端点同样接受 Bearer Token（按操作员权限处理）—— 内置 **API 文档**页（`/apidocs`）提供完整 REST 接口参考及 curl/PHP/Java/PowerShell 示例；`scripts\api-example.ps1` 是开箱即用的 Windows 部署脚本。

## 技术架构

- **后端**：ASP.NET Core (.NET 10)，通过 COM（ICertAdmin2 / ICertView2 / ICertRequest2，强类型 vtable 互操作）管理本机 AD CS；专用 STA 线程执行全部 COM 调用
- **前端**：Vue 3 + Element Plus + ECharts，双语界面（默认英文，可切换中文），构建产物由后端 wwwroot 托管，单端口单服务
- **数据库**：SQLite（Web 用户 / 审计日志 / 设置），零运维
- **部署**：self-contained 发布（服务器无需安装 .NET），注册为 Windows 服务（LocalSystem，默认具备 CA 管理权限）

## 快速开始

```powershell
# 1. 构建（开发机上，需要 Node 20+ 和 .NET 10 SDK）
powershell -File scripts\publish.ps1

# 2. 安装服务（CA 服务器上，管理员 PowerShell）
powershell -File scripts\install-service.ps1            # 默认 HTTP :8442
powershell -File scripts\install-service.ps1 -Port 9000 # 自定义端口

# 3. 访问
# http://<主机名>:8442
# 初始账号 admin，密码见 publish\initial-admin-password.txt（首登强制改密）

# HTTPS：在 系统设置 页配置（模式/端口/证书指纹 —— 保存后自动重启生效），
# 或直接编辑 publish\appsettings.Production.json 的 "Listeners" 节

# 日常更新
powershell -File scripts\update.ps1     # 停服务 → 重建 → 启服务

# 卸载
powershell -File scripts\uninstall-service.ps1
```

## 目录结构

```
CA-Manager\
├── src\CaMgr.Api\        # 后端（COM 互操作/服务/控制器/数据层）
├── src\CaMgr.Web\        # 前端（Vue3 + Element Plus）
├── scripts\              # 发布/安装/更新/卸载/Release 脚本
├── docs\                 # 部署与运维文档（双语）
└── publish\              # 发布产物（服务从此目录运行）
```

## 安全要点

- 高危操作（吊销/CRL 周期/模板启停）强制二次确认并全量审计
- 登录失败 5 次锁定 15 分钟；会话 8 小时滑动过期
- PGP 私钥仅在生成响应中返回一次，服务器不留存任何密钥材料
- API Token 仅 SHA256 哈希存储、创建时一次性展示、日志中永不出现；机器下线时在用户管理页吊销
- 第三方组件在 THIRD-PARTY-NOTICES.md 中登记，每次发布前运行 npm audit + dotnet list --vulnerable 确保无已知高危漏洞
- 建议生产环境启用 HTTPS 并限制防火墙来源（见 docs\deploy.md）

## 已明确排除（保持服务器本地操作）

CA 证书续期、CA 服务启停、数据库维护、AD 模板对象的创建/修改。
