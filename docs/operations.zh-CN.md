# CA-Manager 运维手册

> English | [简体中文](operations.md)

## 角色与权限

| 角色 | 能做什么 |
|------|---------|
| 只读 Viewer | 查询/下载证书与 CRL、查看仪表盘、**提交证书申请（需审批）**、**生成 PGP 密钥对** |
| 操作员 Operator | + 颁发/拒绝/重提交请求、提交 CSR（直接生效）、吊销/取消吊销、发布 CRL、**审批/拒绝普通用户申请** |
| 管理员 Admin | + CRL 周期修改、模板启停、用户管理、审计日志、通知与 LDAP 设置 |

用户来源：本地账号（本系统管理）与 AD 域账号（LDAP 绑定验证）。
域账号角色映射：登录后按 `memberOf` 匹配 `ldap.adminGroup` / `ldap.operatorGroup` 设置的组（**默认均为空**——专为 PAM 托管域管密码的场景设计，无需知道 Domain Admins 密码；未匹配的域账号默认成为可申请证书的只读用户）。推荐做法：在 AD 中创建专用组如 `CA-Manager-Admins` / `CA-Manager-Operators` 并填入对应设置项；本地 `admin` 账号保留为应急后门。
设置入口：管理员 → 用户管理/系统设置 API（`/api/settings`）。

## 审批流程（普通用户申请）

只读用户提交的证书申请（粘贴 CSR 与自助生成两种方式）**不会直接提交 CA**：

1. 申请进入 **"证书审批"** 队列（`/approvals`）。自助生成的私钥以**申请人自设的 PFX 密码加密暂存**（AES-256-GCM），服务器不持有明文。
2. 操作员/管理员批准（此时才真正提交 CA）或拒绝（暂存密钥立即销毁）。
3. 颁发后申请人**凭申请时设置的密码一次性下载 PFX**，下载后暂存私钥立即销毁。
4. 提交/批准/拒绝/下载全程审计，审批人可收到 SMTP 通知（需启用邮件通知）。

## 日常操作对照（Web ↔ certutil）

| Web 操作 | 等效命令 |
|---------|---------|
| 证书列表/筛选 | `certutil -view -restrict "..."` |
| 吊销 | `certutil -revoke <serial> <reason>` |
| 颁发 pending | `certutil -resubmit <RequestId>` |
| 拒绝 | `certutil -deny <RequestId>` |
| 发布 CRL | `certutil -CRL` / `certutil -CRL delta` |
| 提交 CSR | `certreq -submit -attrib "CertificateTemplate:X"` |
| 模板启停 | CA 属性 → 策略模块 Templates 列表 |

## 吊销注意

- 除 `certificateHold` 外吊销不可逆；新 CRL 发布后对全网生效（当前 CRL 下次更新时间见"CA 与 CRL"页）
- `certificateHold` 可取消（取消后证书立即恢复有效）；确认不需要时请改用正式原因重新吊销，避免长期 hold
- 危险确认：需在对话框中输入完整序列号

## CRL 周期

修改后下次发布生效。Delta CRL 设为 0 表示禁用。修改会记录审计。

## 审计

记录项：登录（含失败）/登出/改密/吊销/取消吊销/颁发/拒绝/重提交/提交申请/发布 CRL/配置修改/模板变更/用户增删改。
字段：时间、用户、来源 IP、操作、对象类型、对象、参数摘要、结果。支持按条件查询与 CSV 导出。

## 服务运维

```powershell
Restart-Service CA-Manager      # 重启（改配置后）
Get-EventLog -LogName Application -Source "CA-Manager" -Newest 20   # 应用日志（Windows 事件日志 + 文件日志）
```

日志同时写入 Windows 事件日志；调试级日志含 COM 调用失败详情（HRESULT）。

## 数据库维护

`publish\data\camgr.db`（SQLite）。审计日志较大时可归档导出后由管理员清理旧记录（当前版本不自动清理）。

## 安全基线建议

1. 生产启用 HTTPS，证书用本 CA 签发
2. 防火墙限制来源为运维网段
3. 域账号配置角色映射后禁用多余本地账号
4. 定期导出审计日志归档
5. `initial-admin-password.txt` 首登后删除

## 邮件通知（SMTP）

管理员 → "通知设置"页配置。支持内网匿名中继与加密连接（明文 / STARTTLS 587 / 隐式 SSL 465）。

- **固定收件人**：接收每日汇总（四类事件：证书到期 / CA 证书到期 / CRL 超期 / 待处理积压）
- **申请者通知**：开关开启后，到期证书的申请者邮箱（证书申请时提交的 EMail）会单独收到名下证书清单；机器账户/无邮箱证书自动跳过
- **发送时间**：默认每日 08:00，一天最多一封汇总；"立即执行扫描"可随时手动触发
- **测试**：配置保存后先"发送测试邮件"确认链路，再等定时任务
