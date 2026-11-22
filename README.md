# CA-Manager — Web Management Console for AD CS

> English | [简体中文](README.zh-CN.md)

Move the entire day-to-day operation of Windows AD CS (Active Directory Certificate Services) into the browser — no more remote-desktop sessions on the CA server to run certutil.

## Features

| Module | Capabilities |
|--------|--------------|
| Dashboard | CA status, certificate statistics, 30-day issuance trend, recent operations |
| Certificates | Search (status / keyword / serial / date / template filters, **incl. expired**), **real server-side pagination** (handles 3000+ certificates), detail (SAN / EKU / keys / fingerprints / chain), CER & PEM download, CSV export, **revocation** (7 reasons + effective date + serial-number confirmation), **unrevoke** (certificateHold), **batch revocation** of expired certificates |
| Expiring | 30/60/90-day thresholds, CA certificate expiry alert |
| Requests | Pending / denied / failed queues, **issue / deny / resubmit** |
| Approvals | Viewer submissions queue for operator/admin approval before hitting the CA; self-service keys escrowed encrypted (AES-256-GCM), one-time PFX download |
| New Request | Two modes: **self-service** (server generates the key pair — RSA4096 default; multi-value SAN with mixed DNS + IP; download a ready PFX on issuance) and **paste CSR** (PEM/Base64, private key stays on your machine) |
| PGP Keys | OpenPGP key pairs for file encryption (ECC Ed25519 default, validity 1/2 years, GnuPG-compatible; private key returned once, never stored) + key tracking records with status (active / revoked / lost / compromised) |
| Inventory | Unified asset inventory: CA-issued certificates, PGP keys and third-party imports — owner / assigned-to / notes / lifecycle status, one-click sync from the CA, CSV export, expiry status computation |
| Templates | Templates enabled on the CA (with AD details), admins can enable/disable templates |
| CA & CRL | CA properties / cert chain / CDP / AIA, CRL period view & change, **manual CRL / Delta publish**, CRL download & content parsing, CA certificate download (CER / P7B / zip pack) |
| Notifications | SMTP mail (intranet anonymous / STARTTLS / SSL): daily digest (expiring certs, CA cert, CRL status, pending backlog, **inventory expiry, OCSP health**) + per-requester notices |
| Users | Local accounts + AD domain accounts (LDAP), four-tier RBAC (Admin / Operator / Auditor / User) + **API Tokens** for machine automation |
| Audit Log | Every sensitive operation (logins, revocations, issuance, config changes, PGP generation, inventory changes, token usage…), query & CSV export |
| System | Listener configuration (HTTP / HTTPS / both, ports, certificate thumbprint — applied live), machine certificate store browser, **OCSP Online Responder health monitoring** |
| API Docs | Built-in bilingual REST reference page (`/apidocs`) with copy-paste curl / PHP / Java / PowerShell examples |

## REST API for Machine Automation

Web servers request and renew certificates without a browser:

1. Admin creates one token per machine (**Users** page → **API Tokens** tab) — `cam_`-prefixed, Operator-level, 1-year validity, stored SHA256-hashed
2. Call the endpoints with `Authorization: Bearer cam_...`:

| Endpoint | Purpose |
|----------|---------|
| `POST /api/automation/certificate` | Request a certificate (CN / SAN / key algorithm / template) — returns the PFX on issuance |
| `POST /api/automation/renew` | Re-key renewal by old serial number; old certificate is kept |
| `GET /api/automation/status/{requestId}` | Check request status |

Every other `/api` endpoint also accepts Bearer tokens (at Operator level) — the built-in **API Docs** page (`/apidocs`) documents the complete REST surface with curl / PHP / Java / PowerShell examples; `scripts\api-example.ps1` is a ready-to-use Windows deployment script.

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
powershell -File scripts\install-service.ps1            # default HTTP :8442
powershell -File scripts\install-service.ps1 -Port 9000 # custom port

# 3. Browse
# http://<hostname>:8442
# Initial account: admin — password in publish\initial-admin-password.txt (change forced on first login)

# HTTPS: configure on the System page (mode / port / certificate thumbprint — service restarts automatically),
# or edit the "Listeners" section of publish\appsettings.Production.json

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
- API tokens are stored SHA256-hashed, shown only at creation and never logged; revoke them from the Users page when a machine is decommissioned
- Third-party components are tracked in THIRD-PARTY-NOTICES.md and kept free of known high-severity vulnerabilities (npm audit + dotnet list --vulnerable before each release)
- Enable HTTPS and restrict firewall sources in production (see docs\deploy.md)

## Explicitly Out of Scope (stay local on the server)

CA certificate renewal, starting/stopping the CA service, database maintenance, creation/modification of AD template objects.
