// Endpoint data for the API Docs page: quick-start automation examples and the full REST reference.
// Every row mirrors the controller code (route, authorization, body); text is bilingual { en, zh }
// and picked by the view with curLang().
//
// roles - who can call the endpoint with a browser session, per the role model:
//   public     no authentication
//   any        every signed-in role
//   noAuditor  every signed-in role except Auditor (read-only role)
//   operator   Operator or Admin              admin     Admin only
//   auditor    Admin or Auditor               owner     owner of the record (Operator/Admin act on any row)
//   token      API token - mapped to the Operator role, so every row listing `operator` accepts a token

const R = (method, path, group, roles, en, zh, extra) => ({ id: method + ' ' + path, method, path, group, roles, desc: { en, zh }, ...extra })

const REVOCATION_REASONS = {
  en: 'reason: 0 unspecified · 1 keyCompromise · 2 CACompromise · 3 affiliationChanged · 4 superseded · 5 cessationOfOperation · 6 certificateHold · 8 removeFromCRL. Takes effect network-wide after the next CRL publication.',
  zh: 'reason：0 未指定 · 1 密钥泄露 · 2 CA 泄露 · 3 归属变更 · 4 已替代 · 5 停止运营 · 6 证书冻结 · 8 从 CRL 移除。下一次 CRL 发布后对全网生效。',
}

export const FULL_REFERENCE = [
  // ---------- session ----------
  R('POST', '/api/auth/login', 'gSession', ['public'],
    'Sign in with a local or AD account; the session cookie lasts 8 hours (sliding)',
    '本地或 AD 域账号登录；会话 Cookie 8 小时滑动有效',
    {
      body: `{
  "username": "admin",
  "password": "********"
}`,
      resp: `{
  "username": "admin",
  "role": "Admin",
  "source": "Local",
  "mustChangePassword": false
}`,
      note: { en: '5 failed attempts lock the account for 15 minutes. Not reachable with an API token.', zh: '连续 5 次失败锁定 15 分钟。此端点不接受 API Token。' },
    }),
  R('POST', '/api/auth/logout', 'gSession', ['any'], 'End the current session', '退出当前会话'),
  R('GET', '/api/auth/me', 'gSession', ['any'], 'Current user: username / role / source', '当前登录用户：用户名 / 角色 / 来源',
    { resp: `{
  "username": "zhangsan",
  "role": "Operator",
  "source": "Ad"
}` }),
  R('POST', '/api/auth/change-password', 'gSession', ['any'], "Change the own password (local accounts; AD passwords are managed in AD)", '修改本人密码（仅本地账号；域账号密码在 AD 中修改）',
    { body: `{
  "oldPassword": "current password",
  "newPassword": "new password (min 8 chars)"
}` }),

  // ---------- dashboard ----------
  R('GET', '/api/dashboard', 'gDashboard', ['any'],
    'Statistics, 30-day issuance trend, recent operations; viewers only see their own footprint',
    '统计、近 30 天颁发趋势、最近操作；普通用户只看到自己的数据'),

  // ---------- certificates ----------
  R('GET', '/api/certificates', 'gCerts', ['any'],
    'Paged certificate list; viewers only receive certificates born from their own approved requests',
    '分页证书列表；普通用户只能拿到自己已批准申请产生的证书',
    {
      query: 'status=all|issued|revoked|pending|failed|denied|expiring|expired  ·  keyword  ·  serial  ·  template  ·  from  ·  to  ·  limit=50 (1-200)  ·  page=1  ·  expiringDays=30',
      resp: `{
  "items": [
    { "requestId": 72, "commonName": "www.example.com", "serialNumber": "7A00...021",
      "template": "WebServer", "status": "issued", "notAfter": "2027-11-01T00:00:00" }
  ],
  "totalFetched": 23,
  "truncated": false
}`,
    }),
  R('GET', '/api/certificates/{requestId}', 'gCerts', ['any'],
    'Full detail: subject / SAN / EKU / key / fingerprints / chain', '详情：主题 / SAN / EKU / 密钥 / 指纹 / 证书链'),
  R('GET', '/api/certificates/{requestId}/download', 'gCerts', ['any'], 'Download the issued certificate', '下载已颁发的证书',
    { query: 'format=cer (DER, default) | pem' }),
  R('GET', '/api/certificates/export', 'gCerts', ['any'], 'CSV export of the filtered list (up to 5000 rows)', '按筛选条件导出 CSV（最多 5000 行）',
    { query: 'status  ·  keyword  ·  serial  ·  template  ·  from  ·  to' }),
  R('POST', '/api/certificates/{requestId}/revoke', 'gCerts', ['operator', 'admin'], 'Revoke a certificate', '吊销证书',
    {
      body: `{
  "serialNumber": "7A000000213E3DB47C1EADF31DE000000000021",
  "reason": 4,
  "effectiveFrom": "2026-11-24T00:00:00Z",
  "confirmSerial": "7A000000213E3DB47C1EADF31DE000000000021"
}`,
      note: { en: 'confirmSerial must equal serialNumber (second confirmation). effectiveFrom is optional. ' + REVOCATION_REASONS.en,
              zh: 'confirmSerial 必须与 serialNumber 一致（二次确认）。effectiveFrom 可选。' + REVOCATION_REASONS.zh },
    }),
  R('POST', '/api/certificates/{requestId}/unrevoke', 'gCerts', ['operator', 'admin'],
    'Undo a certificateHold revocation - no other reason can be undone', '取消 certificateHold（证书冻结）原因的吊销——其他原因不可撤销'),
  R('POST', '/api/certificates/batch-revoke', 'gCerts', ['operator', 'admin'], 'Revoke many certificates in one audited call', '一次调用批量吊销多张证书',
    {
      body: `{
  "target": "selected",
  "serials": ["7A00...021", "7A00...022"],
  "reason": 4
}

{
  "target": "expired",
  "reason": 4
}`,
      resp: `{
  "requested": 2,
  "revoked": 2,
  "failed": 0,
  "failures": []
}`,
      note: { en: 'target=expired revokes every issued certificate past its expiry that was never revoked - no serials needed. A selected batch holds at most 2000 serials. Every certificate is audited individually.',
              zh: 'target=expired 一次吊销全部"已过期且未吊销"的证书，无需 serials。selected 模式单批最多 2000 个序列号。每张证书单独记录审计。' },
    }),

  // ---------- requests ----------
  R('GET', '/api/requests/queue', 'gRequests', ['operator', 'admin', 'auditor'], 'Pending / denied / failed request queues', '待颁发 / 已拒绝 / 失败 队列',
    { query: 'status=pending|denied|failed|all  ·  limit=50 (1-200)' }),
  R('POST', '/api/requests/{requestId}/issue', 'gRequests', ['operator', 'admin'], 'Issue a pending request (certutil -resubmit)', '颁发待处理请求（相当于 certutil -resubmit）'),
  R('POST', '/api/requests/{requestId}/deny', 'gRequests', ['operator', 'admin'], 'Deny a pending request', '拒绝待处理请求'),
  R('POST', '/api/requests/{requestId}/resubmit', 'gRequests', ['operator', 'admin'], 'Resubmit a failed request (same as issue)', '重新提交失败请求（等同颁发）'),
  R('POST', '/api/requests/submit', 'gRequests', ['noAuditor'], 'Submit a pasted CSR - the private key stays on the requesting machine', '提交粘贴的 CSR——私钥留在申请方本机',
    {
      body: `{
  "csr": "-----BEGIN CERTIFICATE REQUEST-----...",
  "template": "WebServer"
}`,
      resp: `{
  "pendingApproval": true,
  "approvalId": 12,
  "message": "已提交，等待审批"
}`,
      note: { en: 'Viewers: queued for approval (pendingApproval=true). Operators/Admins: submitted to the CA immediately.',
              zh: '普通用户：进入审批队列（pendingApproval=true）。操作员/管理员：直接提交 CA。' },
    }),
  R('POST', '/api/requests/self-service', 'gRequests', ['noAuditor'], 'Server generates the key pair and submits the request', '服务器生成密钥对并提交申请',
    {
      body: `{
  "commonName": "www.example.com",
  "san": ["www.example.com", "10.0.0.5"],
  "keyAlgorithm": "RSA4096",
  "template": "WebServer",
  "pfxPassword": "YourPfx#2026"
}`,
      note: { en: 'keyAlgorithm: RSA2048 | RSA4096 | ECDSA_P256. Viewers: queued for approval, pfxPassword is required, the PFX is downloaded once from /api/approvals/{id}/pfx. Operators/Admins: issued immediately, the response carries requestId, disposition and pfxBase64.',
              zh: 'keyAlgorithm：RSA2048 | RSA4096 | ECDSA_P256。普通用户：进入审批队列，必须提供 pfxPassword，批准后经 /api/approvals/{id}/pfx 一次性下载。操作员/管理员：立即颁发，响应含 requestId、disposition、pfxBase64。' },
    }),

  // ---------- approvals ----------
  R('GET', '/api/approvals', 'gApprovals', ['any'], 'Approval queue; viewers only see their own submissions', '审批队列；普通用户只看到自己提交的',
    { query: 'status=pending|approved|rejected|all' }),
  R('POST', '/api/approvals/{id}/approve', 'gApprovals', ['operator', 'admin'], 'Approve - the request is submitted to the CA', '批准——申请此时才提交 CA'),
  R('POST', '/api/approvals/{id}/reject', 'gApprovals', ['operator', 'admin'], 'Reject - escrowed key material is wiped', '拒绝——托管的密钥材料立即销毁',
    { body: `{
  "comment": "SAN not owned by the requester"
}` }),
  R('POST', '/api/approvals/{id}/pfx', 'gApprovals', ['owner', 'operator', 'admin'], 'One-time download of an approved self-service PFX', '一次性下载已批准的自助申请 PFX',
    {
      body: `{
  "password": "YourPfx#2026"
}`,
      note: { en: 'password = the PFX password chosen at submission. Returns the binary PFX file; the escrowed key is wiped afterwards.',
              zh: 'password 为提交申请时设置的 PFX 密码。返回二进制 PFX 文件；下载后托管密钥立即销毁。' },
    }),
  R('POST', '/api/approvals/{id}/pgp', 'gApprovals', ['owner', 'operator', 'admin'], 'One-time download of an approved PGP key pair', '一次性下载已批准的 PGP 密钥对',
    { resp: `{
  "publicKeyAsc": "-----BEGIN PGP PUBLIC KEY BLOCK-----...",
  "privateKeyAsc": "-----BEGIN PGP PRIVATE KEY BLOCK-----...",
  "fileNameBase": "zhangsan"
}` }),

  // ---------- CA & CRL ----------
  R('GET', '/api/ca/certificate', 'gCa', ['any'], 'CA certificate: cer (DER), p7b (chain) or zip (both files + import README)', 'CA 证书：cer（DER）/ p7b（证书链）/ zip（两种格式 + 导入说明）',
    { query: 'format=cer (default) | p7b | zip' }),
  R('GET', '/api/ca/info', 'gCa', ['any'], 'CA name / type / CDP / AIA / CRL periods / CA certificate expiry', 'CA 名称 / 类型 / CDP / AIA / CRL 周期 / CA 证书到期时间'),
  R('GET', '/api/ca/crl', 'gCa', ['any'], 'Download the current CRL', '下载当前 CRL',
    { query: 'kind=base (default) | delta' }),
  R('GET', '/api/ca/crl/detail', 'gCa', ['any'], 'Parsed CRL: issuer, this/next update, revoked entries', '解析后的 CRL：颁发者、本次/下次更新时间、吊销条目',
    { query: 'kind=base (default) | delta' }),
  R('POST', '/api/ca/crl/publish', 'gCa', ['operator', 'admin'], 'Publish the full CRL and/or the Delta CRL', '发布完整 CRL 和/或 Delta CRL',
    { body: `{
  "base": true,
  "delta": false
}` }),
  R('POST', '/api/ca/crl/period', 'gCa', ['admin'], 'Change the CRL / Delta CRL publishing period', '修改 CRL / Delta CRL 发布周期',
    {
      body: `{
  "baseUnits": 1,
  "basePeriod": "Weeks",
  "deltaUnits": 1,
  "deltaPeriod": "Days"
}`,
      note: { en: 'Units 1-52 (deltaUnits 0 disables Delta CRL); periods: Hours | Days | Weeks | Months | Years.',
              zh: '单位数 1-52（deltaUnits 为 0 表示禁用 Delta CRL）；周期：Hours | Days | Weeks | Months | Years。' },
    }),

  // ---------- templates ----------
  R('GET', '/api/templates', 'gTemplates', ['any'], 'Templates enabled on the CA, with AD details', 'CA 上已启用的模板（含 AD 详情）'),
  R('POST', '/api/templates/toggle', 'gTemplates', ['admin'], 'Enable / disable a template on the CA', '在 CA 上启用 / 停用模板',
    { body: `{
  "templateName": "WebServer",
  "enable": false
}` }),

  // ---------- PGP ----------
  R('POST', '/api/tools/pgp/generate', 'gPgp', ['noAuditor'], 'Generate an OpenPGP key pair', '生成 OpenPGP 密钥对',
    {
      body: `{
  "name": "Zhang San",
  "email": "zhangsan@example.com",
  "algorithm": "ECC",
  "password": "YourPgp#2026",
  "validityYears": 2
}`,
      resp: `{
  "keyId": "3F2A9C41...",
  "fingerprint": "9C41...",
  "userId": "Zhang San <zhangsan@example.com>",
  "algorithm": "Ed25519 + X25519",
  "expiresAt": "2028-11-23T00:00:00",
  "publicKeyAsc": "-----BEGIN PGP PUBLIC KEY BLOCK-----...",
  "privateKeyAsc": "-----BEGIN PGP PRIVATE KEY BLOCK-----..."
}`,
      note: { en: 'algorithm: ECC (Ed25519 + X25519, recommended) | RSA3072 | RSA4096. Viewers: queued for approval (pendingApproval, approvalId, keyId). Operators/Admins: keys are returned once in this response and never stored.',
              zh: 'algorithm：ECC（Ed25519 + X25519，推荐）| RSA3072 | RSA4096。普通用户：进入审批队列（返回 pendingApproval、approvalId、keyId）。操作员/管理员：密钥仅在本响应中返回一次，服务器不留存。' },
    }),
  R('GET', '/api/pgprecords', 'gPgp', ['any'], 'PGP key tracking records (metadata only); viewers see their own', 'PGP 密钥追踪记录（仅元数据）；普通用户只看自己的',
    { query: 'status=active|revoked|lost|compromised|all' }),
  R('POST', '/api/pgprecords/{id}/status', 'gPgp', ['owner', 'operator', 'admin'], 'Update the status of a PGP key record', '更新 PGP 密钥追踪记录的状态',
    { body: `{
  "status": "revoked",
  "notes": "laptop decommissioned"
}` }),

  // ---------- inventory ----------
  R('GET', '/api/inventory', 'gInventory', ['any'], 'Asset inventory rows (up to 5000); viewers see their own', '资产清单（最多 5000 行）；普通用户只看自己的',
    { query: 'itemType=all|x509|pgp|imported  ·  status=all|active|expired|revoked|retired' }),
  R('POST', '/api/inventory/import', 'gInventory', ['noAuditor'], 'Import a third-party certificate (public part only)', '导入第三方证书（仅公钥部分）',
    { body: `{
  "certificate": "-----BEGIN CERTIFICATE-----..."
}` }),
  R('POST', '/api/inventory/{id}/status', 'gInventory', ['owner', 'operator', 'admin'], 'Set the lifecycle status of an inventory item', '设置清单项的生命周期状态',
    { body: `{
  "status": "retired"
}` }),
  R('POST', '/api/inventory/{id}/meta', 'gInventory', ['owner', 'operator', 'admin'], 'Update owner / assigned-to / notes', '更新负责人 / 使用人 / 备注',
    { body: `{
  "owner": "zhangsan",
  "assignedTo": "web-01",
  "notes": "IIS https binding"
}` }),
  R('POST', '/api/inventory/sync', 'gInventory', ['operator', 'admin'], 'Create / refresh inventory rows for every CA-issued certificate (idempotent)', '为全部 CA 颁发证书创建/更新清单行（幂等）',
    { resp: `{
  "created": 3,
  "message": "同步完成，新建 3 条"
}` }),
  R('GET', '/api/inventory/export', 'gInventory', ['any'], 'CSV export of the filtered inventory', '按筛选条件导出清单 CSV',
    { query: 'itemType  ·  status' }),

  // ---------- API tokens ----------
  R('GET', '/api/apitokens', 'gTokens', ['admin'], 'List tokens - the plaintext is never returned', 'Token 列表——永不返回明文',
    { resp: `[
  { "id": 1, "name": "web-01", "createdBy": "admin", "createdAt": "2026-11-01T08:00:00",
    "expiresAt": "2027-11-01T08:00:00", "lastUsedAt": "2026-11-20T03:10:00",
    "enabled": true, "expired": false }
]` }),
  R('POST', '/api/apitokens', 'gTokens', ['admin'], 'Create a token for one machine', '为一台机器创建 Token',
    {
      body: `{
  "name": "web-01"
}`,
      resp: `{
  "info": { "id": 2, "name": "web-01", "expiresAt": "2027-11-23T08:00:00", "enabled": true },
  "plaintext": "cam_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"
}`,
      note: { en: 'The plaintext is shown exactly once - copy it now. It is stored SHA256-hashed and valid for 1 year.',
              zh: '明文仅显示这一次，请立即复制保存。库中只存 SHA256 哈希，有效期 1 年。' },
    }),
  R('POST', '/api/apitokens/revoke', 'gTokens', ['admin'], 'Revoke a token (machine decommissioned / token leaked)', '吊销 Token（机器退役 / Token 泄露）',
    { body: `{
  "id": 2
}` }),

  // ---------- users ----------
  R('GET', '/api/users', 'gUsers', ['admin'], 'Local and AD accounts: role, source, lock state', '本地与 AD 账号：角色、来源、锁定状态'),
  R('POST', '/api/users', 'gUsers', ['admin'], 'Create a local account', '创建本地账号',
    {
      body: `{
  "username": "zhangsan",
  "password": "At least 8 chars",
  "role": "Operator",
  "displayName": "Zhang San"
}`,
      note: { en: 'role: Viewer | Operator | Admin | Auditor', zh: 'role：Viewer | Operator | Admin | Auditor' },
    }),
  R('PUT', '/api/users/{id}', 'gUsers', ['admin'], 'Change role / enabled / password', '修改角色 / 启用状态 / 密码',
    {
      body: `{
  "role": "Operator",
  "enabled": true,
  "password": null,
  "resetMustChange": false
}`,
      note: { en: 'All fields optional. A password reset is only possible for local accounts (AD passwords live in AD). The built-in admin cannot be demoted or disabled.',
              zh: '字段均可选。只能重置本地账号密码（域账号密码在 AD 中管理）。内置 admin 不能降级或禁用。' },
    }),
  R('DELETE', '/api/users/{id}', 'gUsers', ['admin'], 'Delete an account; the built-in admin is protected', '删除账号；内置 admin 受保护不可删除'),

  // ---------- notifications ----------
  R('GET', '/api/notify/config', 'gNotify', ['admin'], 'SMTP and digest settings - only hasPassword, never the SMTP password', 'SMTP 与汇总设置——只返回 hasPassword，不含 SMTP 密码'),
  R('PUT', '/api/notify/config', 'gNotify', ['admin'], 'Save the full notification configuration', '保存完整的通知配置',
    {
      body: `{
  "smtpEnabled": true,
  "host": "smtp.example.com",
  "port": 587,
  "mode": "starttls",
  "username": "",
  "password": "",
  "from": "ca-manager@example.com",
  "fromName": "CA-Manager",
  "recipients": "pki-team@example.com, ops@example.com",
  "notifyRequester": true,
  "expiringDays": 30,
  "dailyAt": "08:00"
}`,
      note: { en: 'mode: none | starttls | ssl. Send the whole object; an empty password keeps the stored one.',
              zh: 'mode：none | starttls | ssl。请发送完整对象；password 留空则保留原密码。' },
    }),
  R('POST', '/api/notify/test', 'gNotify', ['admin'], 'Send a test mail', '发送测试邮件',
    { query: 'to= (optional; default = the configured recipients)' }),
  R('POST', '/api/notify/run-now', 'gNotify', ['operator', 'admin'], 'Run the daily scan immediately (digest + requester notices)', '立即执行每日扫描（汇总邮件 + 申请者通知）'),

  // ---------- audit ----------
  R('GET', '/api/audit', 'gAudit', ['admin', 'auditor'], 'Query the audit log', '查询审计日志',
    { query: 'action  ·  user  ·  from  ·  to  ·  page=1  ·  pageSize=50' }),
  R('GET', '/api/audit/export', 'gAudit', ['admin', 'auditor'], 'CSV export of the filtered audit log', '按条件导出审计日志 CSV',
    { query: 'action  ·  user  ·  from  ·  to' }),
  R('GET', '/api/audit/actions', 'gAudit', ['admin', 'auditor'], 'Distinct action names (filter dropdown)', '去重后的动作名称（用于筛选）'),

  // ---------- system ----------
  R('GET', '/api/system/endpoints', 'gSystem', ['admin'], 'Listener mode / ports / certificate as currently configured', '当前监听模式 / 端口 / 证书配置'),
  R('GET', '/api/system/ocsp', 'gSystem', ['admin'], 'OCSP responder health: service status, revocation configurations, alerts', 'OCSP 响应器健康：服务状态、吊销配置、告警'),
  R('GET', '/api/system/certs', 'gSystem', ['admin'], 'Certificates in LocalMachine\\My usable for HTTPS (with private key, not expired, not a CA cert)', 'LocalMachine\\My 中可用于 HTTPS 的证书（含私钥、未过期、非 CA 证书）'),
  R('POST', '/api/system/apply', 'gSystem', ['admin'], 'Switch the listener mode / ports / certificate', '切换监听模式 / 端口 / 证书',
    {
      body: `{
  "mode": "https",
  "httpPort": 8442,
  "httpsPort": 8443,
  "thumbprint": "FBDE5326EBE9E045789E998461EB0853424BF7E5"
}`,
      note: { en: 'mode: http | https | both. Rewrites appsettings.Production.json and restarts the service (about 5-10 seconds).',
              zh: 'mode：http | https | both。重写 appsettings.Production.json 并重启服务（约 5-10 秒）。' },
    }),

  // ---------- machine automation ----------
  R('POST', '/api/automation/certificate', 'gAutomation', ['token', 'operator', 'admin'],
    'Request a certificate - issued immediately, PFX returned (examples below)', '申请证书——立即颁发并返回 PFX（示例见下方）'),
  R('POST', '/api/automation/renew', 'gAutomation', ['token', 'operator', 'admin'],
    'Re-key renewal by old serial number - same subject / SAN / template, old certificate kept (examples below)', '按旧序列号换发——沿用主题 / SAN / 模板，旧证书保留（示例见下方）'),
  R('GET', '/api/automation/status/{requestId}', 'gAutomation', ['token', 'operator', 'admin'],
    'Status of a request (examples below)', '查询申请状态（示例见下方）'),
]

export const FULL_REFERENCE_COUNT = FULL_REFERENCE.length

// field tables of the automation examples
export const AUTOMATION_FIELDS = {
  request: [
    { name: 'commonName', en: 'CN of the certificate (required)', zh: '证书通用名称 CN（必填）' },
    { name: 'san', en: 'extra DNS names / IPs (optional; CN is always included)', zh: '附加域名 / IP（可选；CN 始终包含在内）' },
    { name: 'keyAlgorithm', en: 'RSA2048 | RSA4096 | ECDSA_P256 (required)', zh: 'RSA2048 | RSA4096 | ECDSA_P256（必填）' },
    { name: 'template', en: 'CA template name, e.g. WebServer (required; ignored by stand-alone CAs)', zh: 'CA 模板名，如 WebServer（必填；独立 CA 忽略）' },
    { name: 'pfxPassword', en: 'protects the returned PFX (min 8 chars, required)', zh: '保护返回的 PFX（至少 8 位，必填）' },
    { name: 'issued', en: 'true = the PFX is present in pfxBase64', zh: 'true = pfxBase64 中包含 PFX' },
    { name: 'disposition', en: '3 = issued, 5 = pending, 2 = denied by policy', zh: '3 = 已颁发，5 = 待处理，2 = 被策略拒绝' },
  ],
  renew: [
    { name: 'serialNumber', en: 'serial number of the certificate to renew (required)', zh: '要换发的旧证书序列号（必填）' },
    { name: 'pfxPassword', en: 'password for the new PFX (min 8 chars, required)', zh: '新 PFX 的密码（至少 8 位，必填）' },
    { name: 'renewedFrom', en: 'serial of the old certificate', zh: '旧证书序列号' },
    { name: 'note', en: 'the old certificate is kept - revoke it manually if it should no longer be used', zh: '旧证书保留——如需停用请手动吊销' },
  ],
  status: [
    { name: 'requestId', en: 'id in the path', zh: '路径中的请求 ID' },
    { name: 'status', en: 'issued | revoked | pending | denied | failed | expired', zh: 'issued | revoked | pending | denied | failed | expired' },
    { name: 'disposition', en: 'raw AD CS disposition code', zh: 'AD CS 原始状态码' },
  ],
}

export const ROLE_LABEL_KEYS = {
  public: 'apidocs.rPublic', any: 'apidocs.rAny', noAuditor: 'apidocs.rNoAuditor',
  operator: 'apidocs.rOperator', admin: 'apidocs.rAdmin', auditor: 'apidocs.rAuditor',
  owner: 'apidocs.rOwner', token: 'apidocs.rToken',
}
