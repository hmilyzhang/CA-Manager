#!/bin/bash
# CA-Mgr end-to-end API test against live lab CA.
set -e
BASE=http://127.0.0.1:8442
PW=$(grep "初始密码" /d/CA-Mgr/src/CaMgr.Api/bin/Debug/net10.0/initial-admin-password.txt | sed 's/.*: //')

echo "== login =="
curl -s -m 10 -c /tmp/camgr.cookies -H "Content-Type: application/json" \
  -d "{\"username\":\"admin\",\"password\":\"$PW\"}" $BASE/api/auth/login | head -c 90; echo

echo "== templates =="
curl -s -m 60 -b /tmp/camgr.cookies $BASE/api/templates | python -c "
import json,sys
d = json.load(sys.stdin)
print(f'{len(d)} templates')
for t in d[:6]: print(f\"  {t['name']:38} {str(t.get('validityPeriod'))[:20]}\")"

echo "== dashboard =="
curl -s -m 60 -b /tmp/camgr.cookies $BASE/api/dashboard | python -c "
import json,sys
d = json.load(sys.stdin)
print(' alive:', d['alive'], ' totals:', d['totals'], ' caCertExp:', d.get('caCertificateExpiry'))"

echo "== request queue (pending) =="
curl -s -m 60 -b /tmp/camgr.cookies "$BASE/api/requests/queue?status=pending" | python -c "
import json,sys
d = json.load(sys.stdin)
print(f\" pending: {len(d['items'])}\")"

echo "== denied queue =="
curl -s -m 60 -b /tmp/camgr.cookies "$BASE/api/requests/queue?status=denied" | python -c "
import json,sys
d = json.load(sys.stdin)
for i in d['items'][:3]: print(f\"  #{i['requestId']} {i['commonName']} {i['status']} msg={i.get('dispositionMessage')}\")"

echo "== CRL detail =="
curl -s -m 60 -b /tmp/camgr.cookies "$BASE/api/ca/crl/detail?kind=base" | python -c "
import json,sys
d = json.load(sys.stdin)
print(f\" issuer={str(d.get('issuer'))[:40]} revoked={d.get('revokedCount')} nextUpdate={d.get('nextUpdate')}\")"
