# CA-Manager — Web Management Console for AD CS

> English | [简体中文](README.zh-CN.md)

Move the entire day-to-day operation of Windows AD CS (Active Directory Certificate Services) into the browser — no more remote-desktop sessions on the CA server to run certutil.

## Features

| Module | Capabilities |
|--------|--------------|
| Dashboard | CA status, certificate statistics, 30-day issuance trend, recent operations |
| Certificates | Search (status / keyword / serial / date / template filters), detail (SAN / EKU / keys / fingerprints / chain), CER & PEM download, CSV export, **revocation** (7 reasons + effective date + serial-number confirmation), **unrevoke** (certificateHold) |
| Expiring | 30/60/90-day thresholds, CA certificate expiry alert |
| Requests | Pending / denied / failed queues, **issue / deny / resubmit** |
| New Request | Two modes: **self-service** (server generates the key pair; multi-value SAN with mixed DNS + IP; download a ready PFX on issuance) and **paste CSR** (PEM/Base64, private key stays on your machine) |
| PGP Keys | OpenPGP key pairs for file encryption (RSA / ECC, GnuPG-compatible; private key returned once, never stored server-side) |
| Templates | Templates enabled on the CA (with AD details), admins can enable/disable templates |
| CA & CRL | CA properties / cert chain / CDP / AIA, CRL period view & change, **manual CRL / Delta publish**, CRL download & content parsing |
| Notifications | SMTP mail (intranet anonymous / STARTTLS / SSL): daily digest (expiring certs, CA cert, CRL status, pending backlog) + per-requester notices |
| Users | Local accounts + AD domain accounts (LDAP), three-tier RBAC (Admin / Operator / Viewer) |
| Audit Log | Every sensitive operation (logins, revocations, issuance, config changes, PGP generation…), query & CSV export |

## Architecture

- **Backend**: ASP.NET Core (.NET 10) managing the local AD CS through COM (ICertAdmin2 / ICertView2 / ICertRequest2, strongly-typed vtable interop); all COM calls run on a dedicated STA thread
- **Frontend**: Vue 3 + Element Plus + ECharts, bilingual UI (English default, Chinese switchable), served from the backend wwwroot — single service, single port
- **Database**: SQLite (web users / audit log / settings) — zero maintenance
- **Deployment**: self-contained publish (no .NET runtime needed on the server), registered as a Windows service (LocalSystem, which has CA administration rights by default)

## Quick Start

```powershell
# 1. Build (on a build machine; needs Node 20+ and .NET 10 SDK)
powershell -File scripts\publish.ps1

# 2. Install the service (on the CA server, admin PowerShell)
powershell -File scripts\install-service.ps1            # default https://+:8443, falls back to HTTP if cert enrollment fails
powershell -File scripts\install-service.ps1 -UseHttp   # plain HTTP
powershell -File scripts\install-service.ps1 -Port 9000 # custom port

# 3. Browse
# http(s)://<hostname>:8443
# Initial account: admin — password in publish\initial-admin-password.txt (change forced on first login)

# Day-to-day updates
powershell -File scripts\update.ps1     # stop service -> rebuild -> start

# Uninstall
powershell -File scripts\uninstall-service.ps1
```

## Repository Layout

```
CA-Manager\
├── src\CaMgr.Api\        # backend (COM interop / services / controllers / data layer)
├── src\CaMgr.Web\        # frontend (Vue3 + Element Plus)
├── scripts\              # publish / install / update / uninstall / release scripts
├── docs\                 # deployment & operations guides (bilingual)
└── publish\              # publish output (the service runs from here)
```

## Security Notes

- Dangerous operations (revocation / CRL period / template toggling) require explicit confirmation and are fully audited
- 5 failed logins lock the account for 15 minutes; sessions expire after 8 sliding hours
- PGP private keys are returned exactly once in the generation response; no key material is ever stored server-side
- Enable HTTPS and restrict firewall sources in production (see docs\deploy.md)

## Explicitly Out of Scope (stay local on the server)

CA certificate renewal, starting/stopping the CA service, database maintenance, creation/modification of AD template objects.
