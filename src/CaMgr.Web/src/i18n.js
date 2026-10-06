import { createI18n } from 'vue-i18n'

export const messages = {
  zh: {
    nav: {
      dashboard: '仪表盘', certificates: '证书管理', expiring: '到期提醒', requests: '请求处理',
      newRequest: '提交申请', pgp: 'PGP 密钥', templates: '证书模板', ca: 'CA 与 CRL', notify: '通知设置', users: '用户管理', audit: '审计日志',
    },
    common: {
      search: '查询', export: '导出 CSV', detail: '详情', download: '下载', back: '返回',
      cancel: '取消', confirm: '确认', status: '状态', action: '操作', enable: '启用', disable: '停用',
      delete: '删除', days: '天', none: '(无)', never: '从未', submit: '提交',
      requestId: '请求ID', commonName: '通用名称 (CN)', template: '模板', requester: '请求者',
      serialNumber: '序列号', subject: '主题 (Subject)', issuer: '颁发者 (Issuer)', notAfter: '到期时间',
      submittedAt: '提交时间', message: '消息', displayName: '显示名', validity: '有效期', purpose: '用途 (EKU)',
      oid: 'OID', source: '来源', role: '角色', lastLogin: '最近登录', time: '时间', user: '用户',
      ip: '来源 IP', objectType: '对象类型', object: '对象', result: '结果', success: '成功', failed: '失败',
      detailLabel: '详情', revoke: '吊销', unrevoke: '取消吊销', issue: '颁发', deny: '拒绝',
      sessionExpired: '未登录或会话已过期', forbidden: '权限不足', requestFailed: '请求失败',
      truncated: '结果集过大已截断，请细化筛选', matched: '共匹配 {n} 条',
    },
    role: { Viewer: '只读', Operator: '操作员', Admin: '管理员' },
    status: {
      issued: '已颁发', revoked: '已吊销', pending: '待处理', denied: '已拒绝', failed: '失败',
      cacert: 'CA证书', kracert: 'KRA证书', foreign: '外部', other: '其他', all: '全部',
    },
    period: { Hours: '小时', Days: '天', Weeks: '周', Months: '月', Years: '年' },
    caType: { 0: '企业根 CA', 1: '企业从属 CA', 3: '独立根 CA', 4: '独立从属 CA' },
    reason: {
      0: '未指定', 1: '密钥泄露', 2: 'CA 泄露', 3: '从属关系变更', 4: '已被取代',
      5: '停止操作', 6: '证书冻结', 8: '从 CRL 移除', '-1': '已取消吊销',
    },
    actionNames: {
      login: '登录', login_failed: '登录失败', logout: '登出', password_change: '修改密码',
      revoke: '吊销', unrevoke: '取消吊销', issue: '颁发', deny: '拒绝', resubmit: '重新提交',
      submit_request: '提交申请', publish_crl: '发布CRL', ca_config_change: '修改配置',
      template_change: '模板变更', notify_send: '邮件通知', notify_test: '测试邮件', pgp_generate: '生成PGP密钥', user_create: '创建用户', user_update: '更新用户', user_delete: '删除用户',
    },
    login: {
      title: 'CA-Manager 证书服务管理', subtitle: 'Active Directory 证书服务 · Web 管理控制台',
      username: '用户名（本地或域账号）', password: '密码', button: '登 录',
      hint: '支持本地账号与 AD 域账号登录',
      changeTitle: '首次登录 — 修改初始密码', newPassword: '新密码', confirmPwd: '确认',
      save: '保存并进入', pwdLen: '密码至少 8 位', pwdMismatch: '两次输入不一致',
      changed: '密码已修改', enterPw: '请输入用户名和密码',
    },
    logout: '退出登录', language: '语言',
    dashboard: {
      title: '仪表盘', caUnreachable: '无法连接证书颁发机构：{err}',
      issued: '累计已颁发', pending: '待处理请求', revoked: '已吊销', expiring: '30 天内到期',
      trend: '近 30 天颁发趋势', caStatus: 'CA 状态',
      caName: 'CA 名称', caConfig: 'CA 配置串', caCertExpiry: 'CA 证书到期', crlPeriod: 'CRL 周期',
      disabled: '未启用', latestOps: '最新操作',
      daysLeft: '剩余 {n} 天', expiredDays: '已过期 {n} 天',
    },
    certs: {
      title: '证书管理', searchPlaceholder: '搜索 CN / 请求者 / 序列号',
      from: '提交开始', to: '提交结束', revokedReason: '吊销原因',
      revokeTitle: '吊销证书', revokeWarning: '吊销是不可逆操作（certificateHold 除外），新 CRL 发布后全网生效。',
      reasonLabel: '吊销原因', effectiveFrom: '失效日期', effectivePlaceholder: '默认立即生效',
      confirmSerial: '确认序列号', confirmSerialPlaceholder: '输入上方完整序列号以确认',
      confirmRevoke: '确认吊销', revokedOk: '证书已吊销，新 CRL 发布后生效', serialMismatch: '确认序列号不匹配',
      unrevokeConfirm: '确认取消吊销该证书？（仅 certificateHold 可取消）', unrevokeTitle: '取消吊销',
      unrevokedOk: '已取消吊销',
      reasons: {
        0: '未指定 (unspecified)', 1: '密钥泄露 (keyCompromise)', 2: 'CA 泄露 (cACompromise)',
        3: '从属关系变更 (affiliationChanged)', 4: '已被取代 (superseded)',
        5: '停止操作 (cessationOfOperation)', 6: '证书冻结 (certificateHold) — 可取消',
      },
      detail: {
        title: '证书详情', certInfo: '证书信息', sha1: 'SHA1 指纹', validity: '有效期',
        sigAlg: '签名算法', pubKey: '公钥', bits: '位', san: '主体备用名称 (SAN)',
        keyUsage: '密钥用法', eku: '增强密钥用法', chain: '证书链', revokeInfo: '吊销信息',
        pem: 'PEM', operations: '操作', dlCer: '下载 CER (DER)', dlPem: '下载 PEM',
        revokeHint: '吊销（在证书列表操作）', reqInfo: '请求信息', disposition: '处置状态',
        dispositionMsg: '处置消息', csr: 'CSR 解析', key: '密钥',
      },
    },
    expiring: {
      title: '到期提醒', caCertWarn: 'CA 证书将于 {date} 到期（剩余 {days} 天），请尽快安排续期！',
      d30: '30 天内', d60: '60 天内', d90: '90 天内', summary: '共 {n} 张证书即将到期',
      daysLeft: '剩余天数',
    },
    requests: {
      title: '请求处理', newRequest: '提交新申请', empty: '当前队列为空',
      issueConfirm: '确认颁发请求 #{id}（{cn}）？', issueTitle: '颁发确认',
      denyConfirm: '确认拒绝请求 #{id}？', denyTitle: '拒绝确认',
      revokedPlaceholder: '无CN',
    },
    newReq: {
      title: '提交新申请', cardTitle: '提交 CSR（证书签名请求）', template: '证书模板',
      selectTemplate: '选择模板', csr: 'CSR 内容',
      csrPlaceholder: '粘贴 PEM (-----BEGIN NEW CERTIFICATE REQUEST-----) 或 Base64 编码的 PKCS#10 / CMC 请求',
      submitToCa: '提交到 CA', viewQueue: '查看请求队列', precheck: 'CSR 预检',
      pendingParse: '(待提交后解析)', parseFailed: '(Base64 解析失败，请检查格式)',
      result: '提交结果', reqId: '请求 ID', dlCert: '下载证书', viewDetail: '查看详情',
      howto: '如何生成 CSR', opensslTitle: 'OpenSSL：', winTitle: 'Windows certreq：',
      winText: '编写 .inf 文件后执行', needCsr: '请粘贴 CSR 内容', needTemplate: '请选择证书模板',
      submittedOk: '证书已颁发', submittedPending: '请求已提交，等待处理',
      tabSelf: '自助生成（推荐）', tabPaste: '粘贴 CSR',
      sanPh: '输入域名或 IP 后回车，可添加多个', sanAdd: '添加',
      sanEmpty: '未添加 SAN（仅 CN 一项）',
      keyAlg: '密钥算法', genAndIssue: '生成并申请',
      needCn: '请填写通用名称 (CN)',
      pfxPwd: 'PFX 密码', pwdOnce: '密码仅显示这一次，请立即保存',
      dlPfx: '下载 PFX (.pfx 含私钥)', dlCsr: '下载 CSR', dlKey: '下载私钥',
      pendingHint: '请求未立即颁发。请先下载并妥善保管私钥与 CSR，待审批颁发后可自行合成。',
      selfTitle: '自助生成：', selfDesc: '系统代为生成密钥对（含多域名/IP 的 SAN），颁发后直接下载 PFX，可部署到 Web 服务器 / 负载均衡。',
      pasteTitle: '粘贴 CSR：', pasteDesc: '私钥留在您自己的机器上（更安全），粘贴 OpenSSL / certreq 生成的 CSR。',
    },
    templates: {
      title: '证书模板',
      notice: '此处仅管理本 CA 已启用的模板。模板对象的创建与修改请在 Active Directory 中进行。',
      name: '模板名', enableNew: '启用新模板到本 CA', namePlaceholder: '模板名（需已存在于 AD）',
      toggleConfirmOn: '确认向本 CA 启用模板 "{name}"？', toggleConfirmOff: '确认向本 CA 停用模板 "{name}"？',
      toggleTitleOn: '启用模板', toggleTitleOff: '停用模板', enabledOk: '模板已启用', disabledOk: '模板已停用',
    },
    ca: {
      title: 'CA 与 CRL', authority: '证书颁发机构', caName: 'CA 名称', caType: '类型', config: '配置串',
      dns: 'DNS 名', policyModule: '策略模块', version: '版本', caCert: 'CA 证书',
      crlPublish: 'CRL 发布', basePeriod: '基础 CRL 周期', deltaPeriod: 'Delta CRL 周期', disabled: '未启用',
      baseLabel: '基础周期', deltaLabel: 'Delta 周期', deltaHint: '0 = 禁用', savePeriod: '保存 CRL 周期',
      publishBase: '发布基础 CRL', publishBoth: '发布基础 + Delta', dlBase: '下载基础 CRL',
      dlDelta: '下载 Delta CRL', crlContent: '最新 CRL 内容', thisUpdate: '本次更新', nextUpdate: '下次更新',
      revokedCount: '吊销条目', revokedOn: '吊销时间', periodSaveConfirm: '修改 CRL 周期将在下次发布时生效，确认保存？',
      periodTitle: 'CRL 周期', published: 'CRL 发布已触发',
    },
    users: {
      title: '用户管理', create: '新建用户',
      hint: '角色：管理员=全部权限 · 操作员=颁发/吊销/提交 · 只读=仅查询下载',
      local: '本地', ad: 'AD 域', createTitle: '新建本地用户', username: '用户名',
      initPwd: '初始密码', initPwdPlaceholder: '至少 8 位', created: '用户已创建',
      roleUpdated: '角色已更新', resetPwd: '重置密码', resetPrompt: '为用户 {name} 设置新密码（至少 8 位）：',
      resetTitle: '重置密码', pwdReset: '密码已重置', updated: '已更新',
      deleteConfirm: '确认删除用户 {name}？', deleteTitle: '删除用户', deleted: '已删除',
      pwdLen: '密码至少 8 位',
    },
    audit: {
      title: '审计日志', actionType: '操作类型', userContains: '用户名包含', start: '开始', end: '结束',
    },
    pgp: {
      title: 'PGP 密钥生成', hint: '生成用于文件加密的 OpenPGP 密钥对（GnuPG 兼容）。私钥仅在本次响应中返回，服务器不留存任何密钥材料，仅记录审计日志。',
      form: '生成参数', algo: '算法', algoEcc: 'ECC（Ed25519 + X25519，推荐）',
      passphrase: '私钥密码', passphrase2: '确认密码', pwdLen: '至少 8 位，用于保护私钥文件',
      validity: '有效期', never: '永不过期', years: '{n} 年',
      generate: '生成密钥对', needName: '请填写 Name', pwdMismatch: '两次密码不一致', done: '密钥已生成',
      result: '生成结果', fingerprint: '指纹',
      warnOnce: '私钥仅此一次可下载！请立即保存到安全位置；关闭或刷新页面后将无法再获取。',
      dlPub: '下载公钥 (.asc)', dlPriv: '下载私钥 (.asc)', copyPub: '复制公钥', copied: '已复制',
      usage: 'GnuPG 使用示例',
    },
    notify: {
      title: '通知设置', hint: '每日到点自动扫描四类事件（证书到期 / CA 证书 / CRL 状态 / 待处理积压）并发送汇总邮件',
      smtpSection: 'SMTP 邮件服务器', enabled: '启用邮件通知', host: '服务器地址', port: '端口',
      mode: '加密方式', modeNone: '明文（内网匿名）', modeStarttls: 'STARTTLS（587）', modeSsl: 'SSL/TLS 隐式（465）',
      username: '用户名', usernamePh: '匿名请留空', password: '密码', passwordPh: '留空保持不变', passwordSetPh: '已设置', from: '发件人地址', fromName: '发件人显示名',
      ruleSection: '通知规则', recipients: '固定收件人', recipientsPh: '多个邮箱用英文逗号分隔',
      notifyRequester: '通知申请者', expiringDays: '到期阈值（天）', dailyAt: '每日发送时间',
      lastRun: '上次执行', save: '保存配置', sendTest: '发送测试邮件', runNow: '立即执行扫描',
      testTo: '测试邮件发送到：', badAddr: '邮箱地址无效', saved: '配置已保存',
      skipped: '已跳过（未启用或无收件人）', skippedHint: '本次未发送：SMTP 未启用或未配置收件人',
      runDone: '扫描完成', resultLine: '汇总邮件 → {digest} 个收件人；申请者邮件 {req} 封\n到期证书 {exp} 张 | CA 证书告警 {ca} | CRL 告警 {crl} | 待处理 {pending} 条',
    },
  },

  en: {
    nav: {
      dashboard: 'Dashboard', certificates: 'Certificates', expiring: 'Expiring', requests: 'Requests',
      newRequest: 'New Request', pgp: 'PGP Keys', templates: 'Templates', ca: 'CA & CRL', notify: 'Notifications', users: 'Users', audit: 'Audit Log',
    },
    common: {
      search: 'Search', export: 'Export CSV', detail: 'Detail', download: 'Download', back: 'Back',
      cancel: 'Cancel', confirm: 'Confirm', status: 'Status', action: 'Actions', enable: 'Enable', disable: 'Disable',
      delete: 'Delete', days: 'days', none: '(none)', never: 'Never', submit: 'Submit',
      requestId: 'Request ID', commonName: 'Common Name (CN)', template: 'Template', requester: 'Requester',
      serialNumber: 'Serial Number', subject: 'Subject', issuer: 'Issuer', notAfter: 'Expires',
      submittedAt: 'Submitted At', message: 'Message', displayName: 'Display Name', validity: 'Validity', purpose: 'Purpose (EKU)',
      oid: 'OID', source: 'Source', role: 'Role', lastLogin: 'Last Login', time: 'Time', user: 'User',
      ip: 'Source IP', objectType: 'Object Type', object: 'Object', result: 'Result', success: 'Success', failed: 'Failed',
      detailLabel: 'Detail', revoke: 'Revoke', unrevoke: 'Unrevoke', issue: 'Issue', deny: 'Deny',
      sessionExpired: 'Not signed in or session expired', forbidden: 'Permission denied', requestFailed: 'Request failed',
      truncated: 'Result set truncated — refine your filters', matched: '{n} matched',
    },
    role: { Viewer: 'Viewer', Operator: 'Operator', Admin: 'Administrator' },
    status: {
      issued: 'Issued', revoked: 'Revoked', pending: 'Pending', denied: 'Denied', failed: 'Failed',
      cacert: 'CA Cert', kracert: 'KRA Cert', foreign: 'Foreign', other: 'Other', all: 'All',
    },
    period: { Hours: 'hours', Days: 'days', Weeks: 'weeks', Months: 'months', Years: 'years' },
    caType: { 0: 'Enterprise Root CA', 1: 'Enterprise Subordinate CA', 3: 'Standalone Root CA', 4: 'Standalone Subordinate CA' },
    reason: {
      0: 'Unspecified', 1: 'Key Compromise', 2: 'CA Compromise', 3: 'Affiliation Changed', 4: 'Superseded',
      5: 'Cessation of Operation', 6: 'Certificate Hold', 8: 'Remove from CRL', '-1': 'Unrevoked',
    },
    actionNames: {
      login: 'Login', login_failed: 'Login failed', logout: 'Logout', password_change: 'Password change',
      revoke: 'Revoke', unrevoke: 'Unrevoke', issue: 'Issue', deny: 'Deny', resubmit: 'Resubmit',
      submit_request: 'Submit request', publish_crl: 'Publish CRL', ca_config_change: 'Config change',
      template_change: 'Template change', notify_send: 'Mail notification', notify_test: 'Test mail', pgp_generate: 'PGP key generated', user_create: 'User created', user_update: 'User updated', user_delete: 'User deleted',
    },
    login: {
      title: 'CA-Manager Certificate Management', subtitle: 'Active Directory Certificate Services · Web Console',
      username: 'Username (local or domain)', password: 'Password', button: 'Sign In',
      hint: 'Supports local and AD domain accounts',
      changeTitle: 'First Login — Change Initial Password', newPassword: 'New password', confirmPwd: 'Confirm',
      save: 'Save & Continue', pwdLen: 'Password must be at least 8 characters', pwdMismatch: 'Passwords do not match',
      changed: 'Password changed', enterPw: 'Enter username and password',
    },
    logout: 'Sign out', language: 'Language',
    dashboard: {
      title: 'Dashboard', caUnreachable: 'Cannot reach the certification authority: {err}',
      issued: 'Total Issued', pending: 'Pending Requests', revoked: 'Revoked', expiring: 'Expiring in 30 days',
      trend: 'Issuance Trend (30 days)', caStatus: 'CA Status',
      caName: 'CA Name', caConfig: 'CA Config', caCertExpiry: 'CA Certificate Expires', crlPeriod: 'CRL Period',
      disabled: 'Disabled', latestOps: 'Recent Operations',
      daysLeft: '{n} days left', expiredDays: 'expired {n} days ago',
    },
    certs: {
      title: 'Certificates', searchPlaceholder: 'Search CN / requester / serial',
      from: 'Submitted from', to: 'Submitted to', revokedReason: 'Revocation Reason',
      revokeTitle: 'Revoke Certificate', revokeWarning: 'Revocation is irreversible (except certificateHold). It takes effect network-wide once the new CRL is published.',
      reasonLabel: 'Reason', effectiveFrom: 'Effective From', effectivePlaceholder: 'Immediate by default',
      confirmSerial: 'Confirm Serial', confirmSerialPlaceholder: 'Type the full serial number above to confirm',
      confirmRevoke: 'Revoke', revokedOk: 'Certificate revoked; effective after next CRL publication', serialMismatch: 'Serial number does not match',
      unrevokeConfirm: 'Unrevoke this certificate? (only certificateHold can be undone)', unrevokeTitle: 'Unrevoke',
      unrevokedOk: 'Revocation removed',
      reasons: {
        0: 'Unspecified', 1: 'Key Compromise', 2: 'CA Compromise',
        3: 'Affiliation Changed', 4: 'Superseded',
        5: 'Cessation of Operation', 6: 'Certificate Hold — reversible',
      },
      detail: {
        title: 'Certificate Detail', certInfo: 'Certificate', sha1: 'SHA1 Thumbprint', validity: 'Validity',
        sigAlg: 'Signature Algorithm', pubKey: 'Public Key', bits: 'bits', san: 'Subject Alternative Names (SAN)',
        keyUsage: 'Key Usage', eku: 'Enhanced Key Usage', chain: 'Certificate Chain', revokeInfo: 'Revocation',
        pem: 'PEM', operations: 'Actions', dlCer: 'Download CER (DER)', dlPem: 'Download PEM',
        revokeHint: 'Revoke (from certificate list)', reqInfo: 'Request Info', disposition: 'Disposition',
        dispositionMsg: 'Disposition Message', csr: 'CSR Parsed', key: 'Key',
      },
    },
    expiring: {
      title: 'Expiring Reminders', caCertWarn: 'The CA certificate expires on {date} ({days} days left) — plan renewal soon!',
      d30: 'Within 30 days', d60: 'Within 60 days', d90: 'Within 90 days', summary: '{n} certificates expiring',
      daysLeft: 'Days Left',
    },
    requests: {
      title: 'Requests', newRequest: 'New Request', empty: 'Queue is empty',
      issueConfirm: 'Issue request #{id} ({cn})?', issueTitle: 'Issue',
      denyConfirm: 'Deny request #{id}?', denyTitle: 'Deny',
      revokedPlaceholder: 'no CN',
    },
    newReq: {
      title: 'New Request', cardTitle: 'Submit a CSR (Certificate Signing Request)', template: 'Certificate Template',
      selectTemplate: 'Select template', csr: 'CSR Content',
      csrPlaceholder: 'Paste PEM (-----BEGIN NEW CERTIFICATE REQUEST-----) or Base64-encoded PKCS#10 / CMC request',
      submitToCa: 'Submit to CA', viewQueue: 'View request queue', precheck: 'CSR Precheck',
      pendingParse: '(parsed after submission)', parseFailed: '(Base64 parse failed — check format)',
      result: 'Submission Result', reqId: 'Request ID', dlCert: 'Download certificate', viewDetail: 'View detail',
      howto: 'How to generate a CSR', opensslTitle: 'OpenSSL:', winTitle: 'Windows certreq:',
      winText: 'Write an .inf file, then run', needCsr: 'Please paste CSR content', needTemplate: 'Please select a template',
      submittedOk: 'Certificate issued', submittedPending: 'Submitted, pending processing',
      tabSelf: 'Self-service (recommended)', tabPaste: 'Paste CSR',
      sanPh: 'Type a domain or IP and press Enter; add as many as needed', sanAdd: 'Add',
      sanEmpty: 'No SAN entries (CN only)',
      keyAlg: 'Key algorithm', genAndIssue: 'Generate & submit',
      needCn: 'Enter the common name (CN)',
      pfxPwd: 'PFX password', pwdOnce: 'Shown only once — save it now',
      dlPfx: 'Download PFX (.pfx with private key)', dlCsr: 'Download CSR', dlKey: 'Download private key',
      pendingHint: 'Not issued immediately. Download and keep the private key and CSR safe; combine them after approval.',
      selfTitle: 'Self-service:', selfDesc: 'The system generates the key pair (with multi-domain/IP SAN); download a ready PFX for web servers / load balancers.',
      pasteTitle: 'Paste CSR:', pasteDesc: 'Keep the private key on your own machine (more secure); paste a CSR from OpenSSL / certreq.',
    },
    templates: {
      title: 'Certificate Templates',
      notice: 'This page manages templates enabled on this CA only. Create/modify template objects in Active Directory.',
      name: 'Template Name', enableNew: 'Enable a new template on this CA', namePlaceholder: 'Template name (must exist in AD)',
      toggleConfirmOn: 'Enable template "{name}" on this CA?', toggleConfirmOff: 'Disable template "{name}" on this CA?',
      toggleTitleOn: 'Enable Template', toggleTitleOff: 'Disable Template', enabledOk: 'Template enabled', disabledOk: 'Template disabled',
    },
    ca: {
      title: 'CA & CRL', authority: 'Certification Authority', caName: 'CA Name', caType: 'Type', config: 'Config String',
      dns: 'DNS Name', policyModule: 'Policy Module', version: 'Version', caCert: 'CA Certificate',
      crlPublish: 'CRL Publishing', basePeriod: 'Base CRL period', deltaPeriod: 'Delta CRL period', disabled: 'Disabled',
      baseLabel: 'Base period', deltaLabel: 'Delta period', deltaHint: '0 = disabled', savePeriod: 'Save CRL Period',
      publishBase: 'Publish Base CRL', publishBoth: 'Publish Base + Delta', dlBase: 'Download Base CRL',
      dlDelta: 'Download Delta CRL', crlContent: 'Latest CRL Content', thisUpdate: 'This Update', nextUpdate: 'Next Update',
      revokedCount: 'Revoked entries', revokedOn: 'Revoked On', periodSaveConfirm: 'The new CRL period takes effect at next publication. Save?',
      periodTitle: 'CRL Period', published: 'CRL publication triggered',
    },
    users: {
      title: 'User Management', create: 'New User',
      hint: 'Roles: Administrator = full access · Operator = issue/revoke/submit · Viewer = read-only',
      local: 'Local', ad: 'AD Domain', createTitle: 'Create Local User', username: 'Username',
      initPwd: 'Initial Password', initPwdPlaceholder: 'At least 8 characters', created: 'User created',
      roleUpdated: 'Role updated', resetPwd: 'Reset Password', resetPrompt: 'Set a new password for {name} (min 8 chars):',
      resetTitle: 'Reset Password', pwdReset: 'Password reset', updated: 'Updated',
      deleteConfirm: 'Delete user {name}?', deleteTitle: 'Delete User', deleted: 'Deleted',
      pwdLen: 'Password must be at least 8 characters',
    },
    audit: {
      title: 'Audit Log', actionType: 'Action', userContains: 'Username contains', start: 'From', end: 'To',
    },
    pgp: {
      title: 'PGP Key Generation', hint: 'Generate OpenPGP key pairs for file encryption (GnuPG-compatible). Private keys are returned once and never stored; only an audit record is kept.',
      form: 'Parameters', algo: 'Algorithm', algoEcc: 'ECC (Ed25519 + X25519, recommended)',
      passphrase: 'Passphrase', passphrase2: 'Confirm passphrase', pwdLen: 'At least 8 chars, protects the private key file',
      validity: 'Validity', never: 'Never expires', years: '{n} years',
      generate: 'Generate key pair', needName: 'Enter the name', pwdMismatch: 'Passphrases do not match', done: 'Key generated',
      result: 'Result', fingerprint: 'Fingerprint',
      warnOnce: 'The private key can be downloaded only ONCE! Save it somewhere safe now; after leaving this page it cannot be retrieved again.',
      dlPub: 'Download public key (.asc)', dlPriv: 'Download private key (.asc)', copyPub: 'Copy public key', copied: 'Copied',
      usage: 'GnuPG usage',
    },
    notify: {
      title: 'Notifications', hint: 'Scans four event types daily (expiring certs / CA cert / CRL status / pending backlog) and sends a digest mail',
      smtpSection: 'SMTP Server', enabled: 'Enable mail notifications', host: 'Server', port: 'Port',
      mode: 'Encryption', modeNone: 'Plain (intranet anonymous)', modeStarttls: 'STARTTLS (587)', modeSsl: 'Implicit SSL/TLS (465)',
      username: 'Username', usernamePh: 'Leave blank for anonymous', password: 'Password', passwordPh: 'Leave blank to keep', passwordSetPh: '(set)', from: 'From address', fromName: 'From display name',
      ruleSection: 'Notification Rules', recipients: 'Fixed recipients', recipientsPh: 'Multiple addresses, comma separated',
      notifyRequester: 'Notify requesters', expiringDays: 'Expiring (days)', dailyAt: 'Daily send time',
      lastRun: 'Last run', save: 'Save', sendTest: 'Send test mail', runNow: 'Run scan now',
      testTo: 'Send test mail to:', badAddr: 'Invalid address', saved: 'Configuration saved',
      skipped: 'Skipped (disabled or no recipients)', skippedHint: 'Nothing sent: SMTP disabled or no recipients configured',
      runDone: 'Scan finished', resultLine: 'Digest → {digest} recipients; requester mails {req}\nExpiring certs {exp} | CA cert alert {ca} | CRL alert {crl} | pending {pending}',
    },
  },
}

const saved = localStorage.getItem('camgr-lang') || 'en'

export const i18n = createI18n({
  legacy: false,
  locale: saved,
  fallbackLocale: 'zh',
  messages,
})

export function setLang(lang) {
  i18n.global.locale.value = lang
  localStorage.setItem('camgr-lang', lang)
}

export function curLang() {
  return i18n.global.locale.value
}

export function fmtDate(d) {
  if (!d) return '—'
  return new Date(d).toLocaleString(curLang() === 'zh' ? 'zh-CN' : 'en-US', { hour12: false })
}

export function fmtDay(d) {
  if (!d) return '—'
  return new Date(d).toLocaleDateString(curLang() === 'zh' ? 'zh-CN' : 'en-US')
}

const enSingular = { Hours: 'hour', Days: 'day', Weeks: 'week', Months: 'month', Years: 'year' }

export function periodLabel(count, unit) {
  if (curLang() === 'en' && Number(count) === 1) return enSingular[unit] || unit
  return i18n.global.t('period.' + unit)
}
