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
| Port | HTTP 8442 by default (`-Port` to change); HTTPS 8444 by default once switched on in the web UI. Must be allowed through the firewall |

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
"Listeners": { "Mode": "https", "HttpPort": 8442, "HttpsPort": 8444, "Thumbprint": "<SHA1 of a cert in LocalMachine\\My>" }
```

- `Mode`: `http` (default after install) / `https` / `both`; defaults are `HttpPort` 8442 and `HttpsPort` 8444
- The HTTPS certificate is loaded **by thumbprint from the machine's `LocalMachine\My` store** at startup; if it is missing, has no private key or cannot be loaded, the service logs an error and falls back to HTTP on `HttpPort` instead of crash-looping
- Recommended: change it on the **System** page in the web UI (mode radio + port + certificate picker) — saving applies the config and restarts the service automatically, no RDP needed
- Or edit the JSON directly and `Restart-Service CA-Manager`

> **Do not use `netsh http add sslcert`** — Kestrel does not use the http.sys SSL binding, so `netsh sslcert` has no effect here (and an appid with non-hex characters fails with "The parameter is incorrect").

## Firewall

```powershell
New-NetFirewallRule -DisplayName "CA-Manager" -Direction Inbound -Protocol TCP -LocalPort 8442,8444 -RemoteAddress Intranet -Action Allow
```

(Only the port(s) actually configured in `Listeners` are needed.)

## Enabling HTTPS

Issue a server certificate from the local CA (CN = the FQDN users will browse to, imported into `LocalMachine\My` with its private key), then set the thumbprint via the **System** page or `appsettings.Production.json` as shown above:

```powershell
# example: request a WebServer-template certificate and import it
certreq -submit -attrib "CertificateTemplate:WebServer" request.req cert.cer
certreq -accept cert.cer        # lands in LocalMachine\My
Restart-Service CA-Manager      # only when the JSON was edited by hand - the System page restarts the service itself
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

- **HTTPS certificate for CA-Manager itself**: with the default pending-first policy a request stays in the CA queue. Submit it (certreq, or **New Request** in CA-Manager), issue the pending request on the **Requests** page, then retrieve and accept it (`certreq -retrieve <RequestId> cert.cer`, `certreq -accept cert.cer`) - or import the PFX downloaded from CA-Manager into `LocalMachine\My` - and select the certificate on the **System** page.
- **SAN**: stand-alone CAs ignore the SAN request attribute unless the flag `EDITF_ATTRIBUTESUBJECTALTNAME2` is enabled (CA server, admin): `certutil -setreg policy\EditFlags +EDITF_ATTRIBUTESUBJECTALTNAME2` then restart CertSvc. Without it, issued certificates will not contain SAN entries.
- **Templates**: the template dropdown shows "no template needed" — the CertificateTemplate attribute is ignored by stand-alone CAs.
- **Approvals**: with the default pending-first policy every submitted request waits in the CA queue; approve it on the **Requests** page (the in-app approval flow still applies to viewer submissions before that).

## Troubleshooting

| Symptom | Cause & fix |
|---------|-------------|
| Issue/revoke fails with `Access is denied 0x80070005` | Service not running as LocalSystem, or the web user's role is insufficient; check the service account is in CA Administrators |
| Empty lists and `too many active sessions 0x8009400F` in the log | CA database sessions exhausted (leak fixed in current version); restart the CA-Manager service and upgrade |
| CA shows unreachable | Check the CertSvc service is running; local COM depends on RPC |
| Service answers on HTTP although HTTPS is configured | The configured certificate was not found in `LocalMachine\My` (wrong thumbprint, or no private key); the event log shows `HTTPS certificate ... not found - falling back to HTTP`. Pick a valid certificate on the System page and apply |
| Requests from **New Request** / the automation API end as **denied** | The service account (LocalSystem = the CA server's computer account) has no **Enroll** permission on that template. Grant Enroll to `DOMAIN\CA-SERVER$` on the template's Security tab (certtmpl.msc), or on a copy of the template |
| 403 `权限不足` on an endpoint that normally works | The session role is too low for that endpoint - see the role column of the API Docs page (`/apidocs`) |
