# CA-Manager Operations Guide

> English | [简体中文](operations.zh-CN.md)

## Roles & Permissions

| Role | Can do |
|------|--------|
| User (Viewer) | Submit certificate requests (approval required), generate PGP key pairs; sees **only their own** certificates, requests and dashboard statistics |
| Operator | Sees everything globally + issue / deny / resubmit requests, submit CSRs (immediate), revoke / unrevoke, publish CRLs, **approve/reject viewer requests** |
| Auditor | Sees everything globally (certificates, queues, CRL, **audit log**) but performs **no actions** |
| Administrator | Sees everything globally + change CRL periods, enable/disable templates, manage users, notification & LDAP settings |

Data visibility is enforced server-side: viewers can only open certificates originating from their own approved submissions (403 otherwise); the dashboard shows their personal footprint.

Account sources: local accounts (managed here) and AD domain accounts (LDAP bind verification).
Domain role mapping: matched against `memberOf` at login via the `ldap.adminGroup` / `ldap.operatorGroup` settings (both **empty by default** — designed for PAM-managed environments where Domain Admin passwords are not known; unmapped domain users get Viewer with request rights). Recommended: create dedicated groups, e.g. `CA-Manager-Admins` and `CA-Manager-Operators`, and set them in `ldap.adminGroup` / `ldap.operatorGroup`. The local `admin` account stays as the emergency fallback.
Configure on the **Users** page → "AD Directory Integration" card (enable login, domain controller, NetBIOS domain, role-mapping groups, unmapped-user policy).

## Approval Workflow (viewer submissions)

Certificate requests submitted by Viewers (both paste-CSR and self-service generation) do **not** go to the CA directly:

1. The request lands in the **Approvals** queue (`/approvals`). Self-service private keys are escrowed **encrypted with the submitter's own PFX password** (AES-256-GCM) — the server never holds the plaintext.
2. An Operator/Administrator approves (the request is then submitted to the CA) or rejects (escrowed key material is wiped).
3. Once issued, the submitter downloads a **PFX one time** using the password chosen at submission; the escrowed key is wiped immediately after.
4. Every step (submission / approval / rejection / download) is audited, and approvers receive an optional SMTP notification.

## Web ↔ certutil Equivalents

| Web action | Equivalent command |
|------------|--------------------|
| Certificate list / filters | `certutil -view -restrict "..."` |
| Revoke | `certutil -revoke <serial> <reason>` |
| Issue pending | `certutil -resubmit <RequestId>` |
| Deny | `certutil -deny <RequestId>` |
| Publish CRL | `certutil -CRL` / `certutil -CRL delta` |
| Submit CSR | `certreq -submit -attrib "CertificateTemplate:X"` |
| Template enable/disable | CA properties → policy module Templates list |

## Revocation Notes

- Revocation is irreversible except for `certificateHold`; it takes effect network-wide once the new CRL is published (next update time is on the "CA & CRL" page)
- `certificateHold` can be undone (the certificate becomes valid immediately); re-revoke with a permanent reason once you are sure, avoid long-term holds
- Dangerous-action confirmation: the full serial number must be typed into the dialog

## CRL Period

Changes take effect at the next publication. Delta CRL period 0 disables Delta CRLs. All changes are audited.

## Audit

Recorded: logins (including failures) / logout / password changes / revocations / unrevokes / issuance / denials / resubmits / CSR submissions / CRL publications / config changes / template changes / user management.
Fields: time, user, source IP, action, object type, object, parameter summary, result. Filterable queries and CSV export.

## Emergency Admin Password Reset

If the admin password is lost, run from the publish folder on the server (no .NET needed, the self-contained exe handles it; the service may stay running):

```powershell
.\CaMgr.Api.exe reset-admin           # generates a temporary password, forces change at next login
.\CaMgr.Api.exe reset-admin "New#12345678"   # or set a specific one (min 8 chars)
```

## Service Operations

```powershell
Restart-Service CA-Manager      # restart (after config changes)
Get-EventLog -LogName Application -Source "CA-Manager" -Newest 20   # app log (Windows event log + file log)
```

Logs also go to the Windows event log; debug-level entries include COM failure details (HRESULT).

## Database Maintenance

`publish\data\camgr.db` (SQLite). When the audit log grows large, export/archive and prune old records manually (no auto-pruning in the current version).

## Security Baseline

1. Enable HTTPS in production, certificate issued by the local CA
2. Restrict firewall sources to the admin network
3. Once domain role mapping is configured, disable surplus local accounts
4. Export the audit log for archiving regularly
5. Delete `initial-admin-password.txt` after first login

## Email Notifications (SMTP)

Admin → "Notifications" page. Supports intranet anonymous relay and encrypted connections (plain / STARTTLS 587 / implicit SSL 465).

- **Fixed recipients**: receive the daily digest (four event types: expiring certs / CA cert expiry / CRL status / pending backlog)
- **Requester notices**: when enabled, the requester e-mail address (submitted with the certificate request) of expiring certificates receives a personal list; machine accounts / certs without e-mail are skipped automatically
- **Schedule**: daily at 08:00 by default, at most one digest per day; "Run scan now" triggers immediately
- **Test**: save the config, use "Send test mail" to verify the path, then rely on the scheduler
