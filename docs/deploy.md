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
1. Writes the `Listeners` configuration (default `http` on port 8442) into `publish\appsettings.Production.json`
2. Creates the service `CA-Manager` (LocalSystem, auto-start, auto-restart on crash)
3. Starts the service and prints the access URL

## First Login

1. Open `http://<hostname>:8442` (or the port you chose)
2. Initial account `admin`, initial password in `publish\initial-admin-password.txt`
3. A password change is forced on first login; delete the password file afterwards
4. Switch to HTTPS any time on the **System** page (see "Listening Configuration" above)

## Listening Configuration (HTTP / HTTPS)

The service reads a `Listeners` section (Kestrel); the installer writes the initial value into `publish\appsettings.Production.json`:

```json
"Listeners": { "Mode": "https", "HttpPort": 8442, "HttpsPort": 8443, "Thumbprint": "<SHA1 of a cert in LocalMachine\\My>" }
```

- `Mode`: `http` (default after install) / `https` / `both`
- The HTTPS certificate is loaded **by thumbprint from the machine's `LocalMachine\My` store** at startup; if it is missing/unreadable the service falls back to HTTP instead of crash-looping
- Recommended: change it on the **System** page in the web UI (mode radio + port + certificate picker) — saving applies the config and restarts the service automatically, no RDP needed
- Or edit the JSON directly and `Restart-Service CA-Manager`

> **Do not use `netsh http add sslcert`** — Kestrel does not use the http.sys SSL binding, so `netsh sslcert` has no effect here (and an appid with non-hex characters fails with "The parameter is incorrect").

## Firewall

```powershell
New-NetFirewallRule -DisplayName "CA-Manager" -Direction Inbound -Protocol TCP -LocalPort 8442,8443 -RemoteAddress Intranet -Action Allow
```

(Only the port(s) actually configured in `Listeners` are needed.)

## Production HTTPS (if automatic enrollment failed)

Issue a server certificate from the local CA manually (import it into `LocalMachine\My`), then set the thumbprint via the **System** page or `appsettings.Production.json` as shown above and restart the service:

```powershell
# example: request a WebServer-template certificate and import it
certreq -submit -attrib "CertificateTemplate:WebServer" request.req cert.cer
certreq -accept cert.cer        # lands in LocalMachine\My
Restart-Service CA-Manager      # after selecting it on the System page / editing Listeners
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
