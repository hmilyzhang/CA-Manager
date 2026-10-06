# -*- coding: utf-8 -*-
"""E2E: viewer submits self-service cert request with SAN -> admin approves -> viewer downloads PFX."""
import json, urllib.request, base64

BASE = 'http://127.0.0.1:8442'

def session():
    return urllib.request.build_opener(urllib.request.HTTPCookieProcessor())

def call(op, method, url, obj=None, raw=False):
    data = json.dumps(obj, ensure_ascii=False).encode('utf-8') if obj is not None else b''
    req = urllib.request.Request(url, data=data if method != 'GET' else None,
                                 headers={'Content-Type': 'application/json; charset=utf-8'}, method=method)
    with op.open(req, timeout=90) as r:
        if raw:
            return r.read(), r.headers.get('Content-Type'), r.headers
        return json.load(r)

admin = session()
print('admin login:', call(admin, 'POST', BASE + '/api/auth/login', {'username':'admin','password':'Camgr#2026!Lab'})['role'])
user = session()
print('user login:', call(user, 'POST', BASE + '/api/auth/login', {'username':'testuser','password':'Test#12345'})['role'])

# 1) viewer self-service submission (multi SAN)
r = call(user, 'POST', BASE + '/api/requests/self-service', {
    'commonName':'portal.test.lab', 'san':['portal.test.lab','10.3.0.77'],
    'keyAlgorithm':'RSA2048', 'template':'WebServer', 'pfxPassword':'UserPfx#2026'})
print('1) viewer submission:', r)
assert r.get('pendingApproval'), 'expected approval queue'
aid = r['approvalId']

# 2) queue visibility
mine = call(user, 'GET', BASE + '/api/approvals?status=pending')
print('2) user sees own:', [(a['id'], a['username']) for a in mine])
allq = call(admin, 'GET', BASE + '/api/approvals?status=pending')
row = next(a for a in allq if a['id'] == aid)
print('   admin sees:', (row['id'], row['username'], row['commonName'], row['san']))

# 3) wrong password download must fail cleanly (before approve: not approved yet)
try:
    call(user, 'POST', BASE + f'/api/approvals/{aid}/pfx', {'password':'UserPfx#2026'})
    print('3) BUG: download before approve succeeded')
except urllib.error.HTTPError as e:
    print('3) download before approve blocked:', json.load(e)['error'])

# 4) admin approves
res = call(admin, 'POST', BASE + f'/api/approvals/{aid}/approve')
print('4) admin approve:', res)

# 5) viewer downloads PFX with own password (localadm has WebServer enroll rights on this CA)
blob, ct, headers = call(user, 'POST', BASE + f'/api/approvals/{aid}/pfx', {'password':'UserPfx#2026'}, raw=True)
print('5) PFX bytes:', len(blob), '| content-type:', ct)
open(r'D:\CA-Mgr\scripts\testdata\approval-test.pfx', 'wb').write(blob)

# 6) second download must fail (one-time)
try:
    call(user, 'POST', BASE + f'/api/approvals/{aid}/pfx', {'password':'UserPfx#2026'})
    print('6) BUG: second download succeeded')
except urllib.error.HTTPError as e:
    print('6) second download blocked:', json.load(e)['error'])

print('DONE')
