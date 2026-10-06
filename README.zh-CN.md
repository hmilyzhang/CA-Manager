# CA-Manager — AD CS 证书服务 Web 管理系统

> [English](README.md) | 简体中文

把 Windows AD CS（Active Directory 证书服务）的全部日常运维搬到浏览器：不再需要远程桌面登录 CA 服务器执行 certutil。

## 功能总览

| 模块 | 功能 |
|------|------|
| 仪表盘 | CA 状态/证书统计/近 30 天趋势/最新操作 |
| 证书管理 | 查询（状态/关键字/序列号/时间/模板筛选）、详情（SAN/EKU/密钥/指纹/链）、下载 CER/PEM、CSV 导出、**吊销**（7 种原因+失效日期+序列号二次确认）、**取消吊销**（certificateHold） |
| 到期提醒 | 30/60/90 天阈值、CA 证书到期告警 |
| 请求处理 | 待处理/已拒绝/失败队列、**颁发/拒绝/重新提交** |
| 提交申请 | 两种模式：**自助生成**（服务器代生成密钥对，支持 SAN 多域名/IP 混合，颁发后直接下载 PFX）与**粘贴 CSR**（PEM/Base64，私钥留在本机） |
| PGP 密钥 | 生成 OpenPGP 密钥对用于文件加密（RSA/ECC，GnuPG 兼容；私钥仅返回一次，服务器不留存） |
| 证书模板 | 已启用模板列表（含 AD 详情）、管理员启停模板 |
| CA 与 CRL | CA 属性/证书链/CDP/AIA、CRL 周期查看与修改、**手动发布 CRL/Delta**、CRL 下载与内容解析 |
| 通知设置 | SMTP 邮件（内网匿名/STARTTLS/SSL）：每日汇总（证书到期/CA 证书/CRL 状态/待处理积压）+ 申请者单独通知 |
| 用户管理 | 本地账号 + AD 域账号（LDAP），三级 RBAC（管理员/操作员/只读） |
| 审计日志 | 全部敏感操作记录（登录/吊销/颁发/配置变更/PGP 生成…），查询与 CSV 导出 |

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
powershell -File scripts\install-service.ps1            # 默认 https://+:8443，证书申请失败自动回退 HTTP
powershell -File scripts\install-service.ps1 -UseHttp   # 直接 HTTP
powershell -File scripts\install-service.ps1 -Port 9000 # 自定义端口

# 3. 访问
# http(s)://<主机名>:8443
# 初始账号 admin，密码见 publish\initial-admin-password.txt（首登强制改密）

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
- 建议生产环境启用 HTTPS 并限制防火墙来源（见 docs\deploy.md）

## 已明确排除（保持服务器本地操作）

CA 证书续期、CA 服务启停、数据库维护、AD 模板对象的创建/修改。
