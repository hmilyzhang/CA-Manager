# CA-Manager Deployment Guide

> English | [简体中文](deploy.zh-CN.md)

## Prerequisites

| Item | Requirement |
|------|-------------|
| Location | Must run on the AD CS server itself (local COM; remote management of another CA is not supported) |
| OS | Windows Server 2016+ (verified on Server 2025) |
| CA role | Enterprise or Stand-alone AD CS |
| Build environment | Node.js 20+ and .NET 10 SDK (build machine only; not needed on the server) |
| Service account | LocalSystem by default (has CA administration rights locally). For a dedicated account, add it to local `CA Administrators` / `Cert Publishers` and install the service under it |
| Port | 8443 by default (customizable), must be allowed through the firewall |

## Build & Install

```powershell
# Build machine: publish (output in publish\, copy the whole folder to the CA server)
powershell -File scripts\publish.ps1

# CA server (admin PowerShell):
powershell -File scripts\install-service.ps1
```

The install script automatically:
1. Requests an HTTPS certificate from the local CA (WebServer template, CN = host FQDN) and binds it to the port; falls back to HTTP with a warning on failure
2. Writes the listening address into `publish\appsettings.Production.json`
3. Creates the service `CA-Manager` (LocalSystem, auto-start, auto-restart on crash)
4. Starts the service and prints the access URL

## First Login

1. Open `https://<hostname>:8443` (or `http://` when it fell back to HTTP)
2. Initial account `admin`, initial password in `publish\initial-admin-password.txt`
3. A password change is forced on first login; delete the password file afterwards

## Firewall

```powershell
New-NetFirewallRule -DisplayName "CA-Manager" -Direction Inbound -Protocol TCP -LocalPort 8443 -RemoteAddress Intranet -Action Allow
```

## Production HTTPS (if automatic enrollment failed)

Issue a server certificate from the local CA manually, then:

```powershell
$thumb = "<certificate SHA1 thumbprint>"
netsh http add sslcert ipport=0.0.0.0:8443 certhash=$thumb appid="{4d8a5f2e-6b3c-4a9e-9f2e-ca7mgr000001}" certstorename=MY
# then set the Kestrel endpoint in publish\appsettings.Production.json to https://+:8443
Restart-Service CA-Manager
```

## Upgrade / Rollback / Uninstall

```powershell
powershell -File scripts\update.ps1       # upgrade (keeps publish\data)
powershell -File scripts\uninstall-service.ps1   # remove the service (data kept in publish\data)
```

## Data & Backup

- `publish\data\camgr.db`: web users, audit log, settings — include in normal backups
- The audit log is append-only; certificate data itself remains managed by AD CS — this app does not copy the CA database

## Stand-alone CA Notes

On a stand-alone CA (no templates, requests pending by default):

- **HTTPS enrollment at install time**: `certreq -submit` returns "pending" and the installer falls back to HTTP. Issue the pending request from CA-Manager (**Requests** page → **Issue**), then complete it on the server with `certreq -retrieve <RequestId> cert.cer` and `certreq -accept cert.cer`, and switch the binding to HTTPS as shown above.
- **SAN**: stand-alone CAs ignore the SAN request attribute unless the flag `EDITF_ATTRIBUTESUBJECTALTNAME2` is enabled (CA server, admin): `certutil -setreg policy\EditFlags +EDITF_ATTRIBUTESUBJECTALTNAME2` then restart CertSvc. Without it, issued certificates will not contain SAN entries.
- **Templates**: the template dropdown shows "no template needed" — the CertificateTemplate attribute is ignored by stand-alone CAs.
- **Approvals**: with the default pending-first policy every submitted request waits in the CA queue; approve it on the **Requests** page (the in-app approval flow still applies to viewer submissions before that).

## Troubleshooting

| Symptom | Cause & fix |
|---------|-------------|
| Issue/revoke fails with `Access is denied 0x80070005` | Service not running as LocalSystem, or the web user's role is insufficient; check the service account is in CA Administrators |
| Empty lists and `too many active sessions 0x8009400F` in the log | CA database sessions exhausted (leak fixed in current version); restart the CA-Manager service and upgrade |
| CA shows unreachable | Check the CertSvc service is running; local COM depends on RPC |
| HTTPS certificate enrollment failed | Usually WebServer template permissions; configure manually as shown above |
