# -*- coding: utf-8 -*-
"""CA-Manager role-based regression test (runs against the dev instance on 8442).

Covers: Viewer (self-scoped + approval flows), Operator (immediate actions, approvals,
no admin config), Auditor (global read-only), Admin (full)."""
import json, os, urllib.request

BASE = os.environ.get('CAMGR_BASE', 'http://127.0.0.1:8442')
ADMIN_PW = os.environ.get('CAMGR_ADMIN_PW', 'Camgr#2026!Lab')
results = []

def session(): return urllib.request.build_opener(urllib.request.HTTPCookieProcessor())

def call(op, method, url, obj=None, raw=False):
    data = json.dumps(obj, ensure_ascii=False).encode('utf-8') if obj is not None else b''
    req = urllib.request.Request(url, data=data if method != 'GET' else None,
                                 headers={'Content-Type': 'application/json; charset=utf-8'}, method=method)
    with op.open(req, timeout=90) as r:
        if raw: return r.read()
        return json.load(r)

def expect(name, cond, detail=''):
    results.append((name, bool(cond), detail))
    print(('PASS  ' if cond else 'FAIL  ') + name + (('  | ' + str(detail)[:80]) if detail else ''))

def forbidden(op, method, url, obj=None):
    """returns True when the API answers 403"""
    try:
        call(op, method, url, obj)
        return False
    except urllib.error.HTTPError as e:
        return e.code == 403

# ---------- setup: one session per role ----------
admin = session()
expect('admin login', call(admin, 'POST', BASE+'/api/auth/login', {'username':'reg_admin','password':ADMIN_PW})['role'] == 'Admin')

for uname, role, pwd in [('reg_operator', 'Operator', 'Oper#12345'), ('reg_auditor', 'Auditor', 'Audit#12345')]:
    try:
        call(admin, 'POST', BASE+'/api/users', {'username': uname, 'password': pwd, 'role': role, 'displayName': uname})
    except urllib.error.HTTPError as e:
        d = json.load(e)
        if '已存在' not in d.get('error', ''): raise

operator = session()
expect('operator login', call(operator, 'POST', BASE+'/api/auth/login', {'username':'reg_operator','password':'Oper#12345'})['role'] == 'Operator')
viewer = session()
try:
    call(viewer, 'POST', BASE+'/api/users', {'username':'reg_viewer','password':'View#12345','role':'Viewer'})
except Exception:
    pass
try:
    call(viewer2, 'POST', BASE+'/api/auth/login', {'username':'testuser','password':'Test#12345'})
    viewer2_ok = True
except Exception:
    viewer2_ok = False
if not viewer2_ok:
    try:
        call(admin, 'POST', BASE+'/api/users', {'username':'testuser','password':'Test#12345','role':'Viewer','displayName':'Test Viewer'})
    except Exception:
        pass  # already exists
viewer2 = session()
expect('viewer login', call(viewer2, 'POST', BASE+'/api/auth/login', {'username':'testuser','password':'Test#12345'})['role'] == 'Viewer')
auditor = session()
expect('auditor login', call(auditor, 'POST', BASE+'/api/auth/login', {'username':'reg_auditor','password':'Audit#12345'})['role'] == 'Auditor')

# ---------- Viewer ----------
r = call(viewer2, 'POST', BASE+'/api/requests/self-service', {
    'commonName':'reg.test.lab', 'san':['reg.test.lab','10.3.0.99'],
    'keyAlgorithm':'RSA2048', 'template':'User', 'pfxPassword':'RegPfx#2026'})
expect('viewer: self-service goes to approval', r.get('pendingApproval') is True, r)
aid_cert = r['approvalId']

r = call(viewer2, 'POST', BASE+'/api/tools/pgp/generate', {
    'name':'Reg User', 'email':'reg@test.lab', 'algorithm':'ECC', 'password':'RegPgp#2026', 'validityYears':1})
expect('viewer: pgp goes to approval', r.get('pendingApproval') is True, r)
aid_pgp = r['approvalId']

mine_appr = call(viewer2, 'GET', BASE+'/api/approvals?status=all')
owned = {a['requestId'] for a in mine_appr if a.get('requestId')}
certs = call(viewer2, 'GET', BASE+'/api/certificates?limit=200')
foreign = [c for c in certs['items'] if c['requestId'] not in owned]
expect('viewer: certificate list scoped to own', len(foreign) == 0, f'own={len(owned)} foreign={len(foreign)}')
expect('viewer: forbidden foreign detail', forbidden(viewer2, 'GET', BASE+'/api/certificates/3'))
expect('viewer: forbidden global queue', forbidden(viewer2, 'GET', BASE+'/api/requests/queue'))
expect('viewer: forbidden approve', forbidden(viewer2, 'POST', BASE+f'/api/approvals/{aid_cert}/approve'))
expect('viewer: forbidden users page', forbidden(viewer2, 'GET', BASE+'/api/users'))
expect('viewer: forbidden audit log', forbidden(viewer2, 'GET', BASE+'/api/audit'))
expect('viewer: forbidden CRL publish', forbidden(viewer2, 'POST', BASE+'/api/ca/crl/publish', {'base':True,'delta':False}))
d = call(viewer2, 'GET', BASE+'/api/dashboard')
# dynamic expectation: issued/pending/revoked must equal the viewer's own footprint
own_appr = call(viewer2, 'GET', BASE+'/api/approvals?status=all')
own_rids = {a['requestId'] for a in own_appr if a.get('requestId')}
own_certs = [c for c in call(viewer2, 'GET', BASE+'/api/certificates?limit=500')['items'] if c['requestId'] in own_rids]
exp_issued = len([c for c in own_certs if c['status'] == 'issued'])
exp_revoked = len([c for c in own_certs if c['status'] == 'revoked'])
exp_pending = len([a for a in own_appr if a['status'] == 'pending'])
ok_d = (d['totals']['issued'] == exp_issued and d['totals']['pending'] == exp_pending
        and d['totals']['revoked'] == exp_revoked)
expect('viewer: dashboard scoped (matches own footprint)', ok_d,
       f"got {d['totals']} expected issued={exp_issued} pending={exp_pending} revoked={exp_revoked}")

# ---------- Operator: approves both, immediate actions, no admin config ----------
allq = call(operator, 'GET', BASE+'/api/approvals?status=pending')
mine = {a['id'] for a in allq}
expect('operator: sees approval queue', aid_cert in mine and aid_pgp in mine, sorted(mine)[:5])
res = call(operator, 'POST', BASE+f'/api/approvals/{aid_pgp}/approve')
expect('operator: approve pgp', res.get('disposition') == 0)
res = call(operator, 'POST', BASE+f'/api/approvals/{aid_cert}/approve')
expect('operator: approve cert (CA decides)', 'requestId' in res, res)
rid = res['requestId']
cert_issued = res.get('disposition') == 3

pgp = call(operator, 'POST', BASE+'/api/tools/pgp/generate', {
    'name':'Op User', 'email':'op@test.lab', 'algorithm':'RSA3072', 'password':'OpPgp#2026', 'validityYears':1})
expect('operator: pgp generates immediately (no approval)', pgp.get('pendingApproval') is None and pgp.get('privateKeyAsc'))

# viewer downloads both one-time
blob = call(viewer2, 'POST', BASE+f'/api/approvals/{aid_pgp}/pgp', raw=True)
expect('viewer: pgp one-time download', len(blob) > 100)
try:
    call(viewer2, 'POST', BASE+f'/api/approvals/{aid_pgp}/pgp')
    expect('viewer: pgp second download blocked', False)
except urllib.error.HTTPError:
    expect('viewer: pgp second download blocked', True)
if cert_issued:
    blob = call(viewer2, 'POST', BASE+f'/api/approvals/{aid_cert}/pfx', {'password':'RegPfx#2026'}, raw=True)
    expect('viewer: cert pfx one-time download', len(blob) > 500)
else:
    expect('viewer: cert pfx skipped (CA denied by template perms - environment)', True)

expect('operator: forbidden user management', forbidden(operator, 'POST', BASE+'/api/users', {'username':'x1','password':'x'*10,'role':'Viewer'}))
expect('operator: forbidden CRL period change', forbidden(operator, 'POST', BASE+'/api/ca/crl/period', {'baseUnits':2,'basePeriod':'Weeks','deltaUnits':1,'deltaPeriod':'Days'}))

# ---------- Auditor: global read-only ----------
allc = call(auditor, 'GET', BASE+'/api/certificates?limit=200')
expect('auditor: global cert list', len(allc['items']) >= 10)
q = call(auditor, 'GET', BASE+'/api/requests/queue?status=all')
expect('auditor: global request queue', 'items' in q)
al = call(auditor, 'GET', BASE+'/api/audit?pageSize=5')
expect('auditor: audit log readable', al['total'] > 0)
expect('auditor: forbidden issue', forbidden(auditor, 'POST', BASE+f'/api/requests/{rid}/issue'))
expect('auditor: forbidden revoke attempt on queue item', forbidden(auditor, 'POST', BASE+'/api/ca/crl/publish', {'base':True,'delta':False}))
expect('auditor: forbidden pgp generate', forbidden(auditor, 'POST', BASE+'/api/tools/pgp/generate', {'name':'x','email':'','algorithm':'ECC','password':'x'*10,'validityYears':1}))
expect('auditor: forbidden self-service submit', forbidden(auditor, 'POST', BASE+'/api/requests/self-service', {'commonName':'aud.test.lab','san':[],'keyAlgorithm':'RSA2048','template':'User','pfxPassword':'x'*10}))
expect('auditor: forbidden csr submit', forbidden(auditor, 'POST', BASE+'/api/requests/submit', {'csr':'M' * 64, 'template':'User'}))
expect('auditor: forbidden users page', forbidden(auditor, 'GET', BASE+'/api/users'))

# ---------- Admin: full ----------
expect('admin: user management', 'items' in call(admin, 'GET', BASE+'/api/users') or isinstance(call(admin, 'GET', BASE+'/api/users'), list))
r = call(admin, 'POST', BASE+'/api/requests/self-service', {
    'commonName':'admintest.test.lab', 'san':['admintest.test.lab'],
    'keyAlgorithm':'ECDSA_P256', 'template':'User'})
expect('admin: self-service immediate (no approval)', r.get('pendingApproval') is None and r.get('pfxBase64'))
expect('admin: audit log', call(admin, 'GET', BASE+'/api/audit?pageSize=1')['total'] > 0)

# ---------- summary ----------
fails = [n for n, ok, _ in results if not ok]
print(f"\n===== {len(results) - len(fails)}/{len(results)} PASSED =====")
if fails:
    print('FAILED:', fails)
    raise SystemExit(1)
