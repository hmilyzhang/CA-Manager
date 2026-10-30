# CA-Manager Certificate Operations BAU SOP

> [简体中文](bau-sop.zh-CN.md) | English
>
> Standard Operating Procedure for day-to-day (Business As Usual) certificate operations performed through the CA-Manager web UI.
> All operation paths follow the actual system menus. Deployment, architecture and troubleshooting details: [Operations Manual](operations.md).

## 1. Document Control

| Item | Content |
|------|---------|
| Document version | v1.0 |
| Effective date | 2026-10-19 |
| Applies to | CA-Manager v1.6.x (web management system for AD CS certificate services) |
| Review cycle | Every 6 months; within 2 weeks after any major system upgrade |
| Author / Reviewer / Approver | [Name / Date] |

## 2. Purpose & Scope

**Purpose**: standardize day-to-day certificate operations carried out through CA-Manager, define standard steps, completion criteria and escalation paths for each role, and ensure continuity of certificate services and full auditability of operations.

**In scope (performed via CA-Manager)**: certificate requests and approvals, issuing/denying, revocation and un-revocation, CRL publishing and validity-period management, expiring-certificate follow-up, certificate template enable/disable, user and permission management, audit log archiving.

**Out of scope (still handled locally on the CA server / in AD — escalate per Section 7)**:

- Renewal of the enterprise CA's own certificate
- Starting/stopping the AD CS service (CertSvc)
- AD CS database maintenance and backup
- Creating/modifying certificate template objects in AD (CA-Manager can only enable/disable templates that already exist)

## 3. Roles & Responsibilities

| Role | System permissions | BAU responsibilities |
|------|--------------------|----------------------|
| User | Submit requests (approval required), generate PGP keys; sees only own certificates | Submit requests as needed, keep PFX/private-key passphrases safe, re-request before expiry |
| Operator | Global read + issue/deny/resubmit, revoke/un-revoke, publish CRL, approve user requests | **Primary owner of daily operations**: clear approval and request queues, perform revocations, publish CRLs, follow up on expiring certificates |
| Auditor | Global read-only + audit log | Monthly spot checks of audit records, verifies routine tasks were completed |
| Admin | Operator permissions + CRL validity periods, template enable/disable, user management, notification/LDAP settings | Account and permission maintenance, CRL period changes, notification pipeline upkeep, oversight of audit archiving |
| Platform/AD administrator (escalation) | Local access to CA server | Out-of-scope items (CertSvc, CA certificate renewal, AD template objects, database backup) |

> Recommended: grant roles via AD group mapping (`CA-Manager-Admins` / `CA-Manager-Operators`); keep the local `admin` account as an emergency backdoor only.

## 4. Routine Operations Calendar

### 4.1 Every business day (Operator)

| # | Task | Where | Completion criteria |
|---|------|-------|---------------------|
| D1 | After 08:00, read the daily digest email (4 event types: expiring certs / CA certificate expiry / CRL overdue / approval backlog) | Mailbox | Every reported event is routed into the matching procedure; email missing → handle per EXP-05 |
| D2 | Clear the **Approvals** (`/approvals`) queue: approve or deny | Approvals | Queue cleared same day, or backlog ≤ [N] items with reasons logged |
| D3 | Handle pending / failed requests in **Requests** (`/requests`) | Requests | Pending requests resubmitted or denied; failed request causes categorized (summarized in W2) |
| D4 | Check the **Dashboard** (`/dashboard`): CA status, pending count, expiring-in-30-days count | Dashboard | No red alerts; any alert routed into the matching procedure |

### 4.2 Weekly (Operator)

| # | Task | Where | Completion criteria |
|---|------|-------|---------------------|
| W1 | Review the 30-day expiring list on **Expiring** (`/expiring`); confirm each owner has responded on renewal (the system emails requesters automatically — this is the manual safety net) | Expiring | Every certificate on the list has a recorded disposition (reissue / retire) in [tracker/ticket] |
| W2 | Summarize causes of the week's failed requests and denied applications | Requests | Systematic patterns (wrong template, non-compliant SAN, ...) fed back to requesters or escalated to Admin |
| W3 | On **CA & CRL** (`/ca`), verify the CRL next-update time is normal, not overdue | CA & CRL | CRL not overdue; if overdue → handle per EXP-04 |

### 4.3 Monthly (Operator + Auditor)

| # | Task | Owner | Completion criteria |
|---|------|-------|---------------------|
| M1 | Export last month's **Audit Log** CSV and archive it | Operator | CSV stored in [archive path]; Auditor spot-checks |
| M2 | Account review: disable departed/transferred users, clean up dormant accounts | Admin | No redundant accounts in Users (`/users`) |
| M3 | Download the current CRL from **CA & CRL** and inspect its parsed content; confirm revocation entries and publishing period are normal | Auditor | Findings recorded |
| M4 | Check `publish\data\camgr.db` size and free disk space on the CA server | Admin | Disk has headroom; if the audit tables are bloated, archive then clean old records |

### 4.4 Quarterly (Admin + Auditor)

| # | Task | Completion criteria |
|---|------|---------------------|
| Q1 | Permission review: members of the AD role-mapping groups (`ldap.adminGroup` / `ldap.operatorGroup`) match actual job assignments; local accounts kept to a minimum | Matches the staffing list |
| Q2 | Security baseline check: HTTPS enabled, firewall restricted to the ops network, `initial-admin-password.txt` deleted, login lockout in effect | Baseline checklist signed off |
| Q3 | Emergency drill: rehearse admin password reset (EXP-06) and service restart (EXP-02) | Drill record kept |

## 5. Standard Operating Procedures

General conventions:

- URL: `https://<hostname>:8443`; 5 failed logins lock the account for 15 minutes; sessions expire after 8 hours of sliding inactivity.
- All sensitive operations (revocation, issuing, configuration changes) are written to the audit log automatically — no manual logging needed; business context (e.g. "owner requested early rotation") goes into [tracker/ticket].
- For any operation involving private keys (self-service generation, PFX download): the requester must take possession immediately; the server never retains plaintext key material.

### SOP-01 Approve and issue a user's certificate request

- **Trigger**: a new request appears in the approval queue (a regular user submitted via **New Request**, either pasting a CSR or using self-service generation).
- **Permissions**: Operator / Admin.
- **Steps**:
  1. Open **Approvals** (`/approvals`) and inspect the request: requester, purpose, template, subject and SAN, validity.
  2. **Verify compliance**: purpose matches the template, SAN domains belong to the requester's organization, no unusual wildcards or excessive validity. When in doubt, confirm with the requester through an out-of-band channel first.
  3. On approval click **Approve** — only now is the request actually submitted to the CA for issuing (for self-service requests, the staged private key is held encrypted with the requester's own passphrase, AES-256-GCM).
  4. For non-compliant requests click **Deny** with a reason; the staged key is destroyed immediately and the requester sees the result.
  5. Remind the requester: once issued, they download the PFX from **Certificates** with a **one-time download** using the passphrase they set; the server-side staged key is destroyed after download.
- **Completion criteria**: the request shows approved/denied; an issued certificate is findable on **Certificates**.
- **Cautions**: in CSR mode the private key stays on the requester's machine — PFX applies only to self-service mode. Never approve a SAN unrelated to the requester/their department.

### SOP-02 Handle pending / failed requests (Requests queue)

- **Trigger**: pending (awaiting CA-side action, typically manager-approval templates) or failed requests on **Requests** (`/requests`).
- **Permissions**: Operator / Admin.
- **Steps**:
  1. Inspect each pending request's RequestId and template; if it should indeed be issued → click **Issue** (equivalent to `certutil -resubmit`); if not → **Deny**.
  2. For failed requests read the failure cause; requester-side problems go back for resubmission, template/CA configuration problems escalate to Admin.
  3. Repeated failures: record RequestId and HRESULT and escalate per Section 7.
- **Completion criteria**: queue cleared same day or reason documented.

### SOP-03 Operator direct submission (no approval)

- **Trigger**: ops-side certificates (e.g. server certificates) to be issued directly.
- **Permissions**: Operator / Admin (an Operator's CSR submission takes effect immediately, bypassing the approval queue).
- **Steps**:
  1. Go to **New Request** (`/new-request`) and pick a mode:
     - **Paste CSR**: private key stays on the requesting machine; paste the PEM/Base64 CSR, pick the template, submit;
     - **Self-service generation**: the server generates the key pair (mixed multi-domain/IP SAN supported); set a PFX passphrase and download the PFX after issuance.
  2. Find the result on **Certificates**; CER/PEM can be downloaded there.
- **Completion criteria**: certificate status is issued; self-service PFX downloaded and passphrase delivered.
- **Cautions**: in self-service mode the key is generated in server memory — suitable for internal/test use; production server certificates should use CSR mode.

### SOP-04 Revoke a certificate

- **Trigger**: private key compromise, host decommission, incorrect certificate contents, or an order to stop using the certificate.
- **Permissions**: Operator / Admin.
- **Steps**:
  1. Find the certificate on **Certificates** (`/certificates`) by serial number/keyword, open the details and verify subject and fingerprint — **confirm it is the right certificate**.
  2. Click **Revoke**, choose one of the 7 reasons, optionally set an effective date.
  3. **Type the full serial number** in the confirmation box to complete the double confirmation.
  4. On **CA & CRL** (`/ca`), check the current CRL's next-update time; if it must take effect immediately, publish the CRL manually per SOP-05.
- **Completion criteria**: certificate status becomes revoked; the revocation takes effect network-wide once the new CRL is published.
- **Risks & cautions**:
  - **Revocation is irreversible for every reason except `CertificateHold`** — a mistaken revocation can only be fixed by reissuing. Verify the fingerprint before acting.
  - Key-compromise incidents must also be reported to [security team] per the security incident process.

### SOP-05 Publish CRL / Delta CRL manually

- **Trigger**: make a revocation effective immediately; anomaly found in the routine CRL check; landing a just-changed publishing period.
- **Permissions**: Operator / Admin.
- **Steps**:
  1. Open **CA & CRL** (`/ca`); review the current CRL period and next-update time.
  2. Click **Publish CRL** (full CRL; also publish the Delta CRL when a shorter revocation-effect window is needed — equivalent to `certutil -CRL` / `certutil -CRL delta`).
  3. Download the CRL and inspect its parsed content: the new revocation is included and the next-update time is correct.
- **Completion criteria**: CRL serial/ThisUpdate advanced and the target certificate appears in the revocation list.
- **Cautions**: publishing pushes the CRL to all clients that rely on the CDP; avoid frequent publishing during business peak hours.

### SOP-06 Un-revoke (CertificateHold only)

- **Trigger**: a certificate temporarily revoked with `CertificateHold` is confirmed fit for use again.
- **Permissions**: Operator / Admin.
- **Steps**:
  1. Find the certificate on **Certificates** → **Un-revoke** (restores validity immediately).
  2. If a published CRL already contained the revocation, publish the CRL again (SOP-05) so the restoration propagates.
- **Completion criteria**: certificate back to valid status.
- **Cautions**: `CertificateHold` is a short-term measure only; a certificate confirmed permanently unwanted should be re-revoked with a definitive reason instead of being left on hold.

### SOP-07 Change CRL publishing periods (Admin)

- **Trigger**: security policy change (e.g. shortening the revocation-effect window) or an unsuitable current period.
- **Permissions**: Admin.
- **Steps**:
  1. On **CA & CRL** (`/ca`), change the CRL / Delta CRL periods (Delta = 0 disables Delta CRLs).
  2. Publish the CRL once manually so the new period takes effect immediately (SOP-05).
  3. Confirm the displayed next-update time reflects the new period.
- **Completion criteria**: new period in effect with an audit record; clients (via the CDP distribution point) can retrieve the new CRL.
- **Cautions**: overly short periods increase client fetch load; confirm the baseline with the security team before changing.

### SOP-08 Enable/disable certificate templates (Admin)

- **Trigger**: a new template goes live (the object itself was created in AD by the platform administrator) or a template must stop issuing.
- **Permissions**: Admin.
- **Steps**:
  1. On **Templates** (`/templates`), review enabled templates and their AD details.
  2. Enable/disable the target template (equivalent to editing the policy module's Templates list in CA properties).
  3. Notify affected requesters of the change and its effective time.
- **Completion criteria**: template list state correct; audit record present.
- **Cautions**: creating/modifying template objects happens in AD and is out of scope (escalate to the platform/AD administrator).

### SOP-09 Expiring-certificate renewal follow-up

- **Trigger**: expiring events in the daily digest / the 30/60/90-day lists on **Expiring** (`/expiring`).
- **Permissions**: Operator / Admin (follow-up); User (re-request).
- **Note**: CA-Manager does not auto-renew; renewal means the requester **submits a new request** before expiry (the old certificate simply lapses — no revocation needed).
- **Steps**:
  1. Weekly, review/export the 30-day expiring list (task W1).
  2. Confirm notification emails reached requesters (machine accounts / certificates without email are skipped automatically — locate the owner manually).
  3. When the owner re-requests, process via SOP-01; if explicitly retired, record it and let the certificate lapse.
  4. Watch the **CA certificate expiry alert**: the CA's own certificate is out of scope — escalate to the platform administrator at least [30] days ahead.
- **Completion criteria**: every certificate on the list has a disposition (reissued / retired).

### SOP-10 Generate a PGP key pair

- **Trigger**: a user needs an OpenPGP key pair for file encryption.
- **Permissions**: everyone except Auditor.
- **Steps**:
  1. On **PGP Keys** (`/pgp`), choose algorithm (RSA/ECC) and size, then generate.
  2. **The private key is returned once, in the generation response only** — save it immediately; it cannot be retrieved after leaving the page, and the server retains no key material.
  3. A lost private key means regenerating and re-distributing the public key.
- **Completion criteria**: public key distributed, private key stored offline.

### SOP-11 Audit log queries and monthly archiving

- **Trigger**: security spot checks, incident forensics, monthly routine (M1).
- **Permissions**: Auditor / Admin see everything; filter by time, user, operation type etc., with CSV export.
- **Steps**:
  1. On **Audit Log** (`/audit`), filter (e.g. all revocations last month).
  2. Export CSV to [archive path], named e.g. `camgr-audit-2026-09.csv`.
  3. Cross-check a sample of entries against tickets/trackers.
- **Completion criteria**: archive complete and logged in the monthly checklist; retention ≥ [12 months].

### SOP-12 User account provisioning / change / deprovisioning

- **Trigger**: onboarding, transfer, departure; role changes.
- **Permissions**: Admin.
- **Steps**:
  1. On **Users** (`/users`), create a local account, or enable AD domain login and let domain accounts sign in directly (roles mapped via `memberOf` against the configured groups).
  2. Assign the least-privileged role: view-only → User; approvals/revocation → Operator; compliance oversight → Auditor.
  3. Departure/transfer: disable the local account immediately; for domain accounts, revoke by removing them from the role-mapping group (or disabling the domain account).
  4. New admin accounts are forced to change password at first login; delete `initial-admin-password.txt` right after first login.
- **Completion criteria**: accounts match staffing; monthly M2 review passes.

## 6. Service & Configuration Changes (low-frequency BAU)

| Change | Action | Permission / location |
|--------|--------|-----------------------|
| Configure email notifications | **Notifications** (`/notify`): set up SMTP → **Send test email** to verify → enable daily digest and requester notifications | Admin |
| Trigger the expiry scan manually | **Run scan now** on the Notifications page | Admin |
| Restart the CA-Manager service | On the CA server: `Restart-Service CA-Manager` (after config changes) | Platform administrator |
| View application logs | Event Log source `CA-Manager` (or file logs), includes COM HRESULT details | Platform administrator |
| Emergency admin password reset | See EXP-06 | Platform administrator |

## 7. Exception Handling & Escalation

### 7.1 Escalation path

| Level | Contact | Details |
|-------|---------|---------|
| L1 | Certificate operator on duty | [Name / Phone] |
| L2 | CA-Manager system administrator | [Name / Phone] |
| L3 | Platform/AD administrator (CA server, CertSvc, AD templates) | [Name / Phone] |

### 7.2 Common exceptions

| ID | Symptom | Handling |
|----|---------|----------|
| EXP-01 | Approval backlog above threshold ([N] items / 24 hours) | Operator reinforcements clear the queue; sustained backlog → escalate to L2 to assess adding operators |
| EXP-02 | Web UI unreachable / login broken | Platform administrator runs `Restart-Service CA-Manager` on the CA server; check Event Log source `CA-Manager`; unresolved → L3 |
| EXP-03 | Certificate revoked by mistake | If the reason was `CertificateHold` → un-revoke per SOP-06; any other reason is **irrecoverable** — reissue immediately per SOP-01/SOP-03 and inform the certificate's users |
| EXP-04 | CRL overdue alert (digest email / CA & CRL page) | Publish the CRL manually first (SOP-05); if publishing fails, CertSvc is unhealthy → escalate to L3 (out of scope, requires local server work) |
| EXP-05 | Daily digest email not received | Admin checks **Notifications**: SMTP settings, send time, recipients; use **Run scan now** and **Send test email** to verify the pipeline |
| EXP-06 | admin password forgotten | In the publish folder on the CA server run `.\CaMgr.Api.exe reset-admin` (prints a temporary password, forced change at next login; the service may keep running) |
| EXP-07 | Account locked (5 failed logins, 15-minute lock) | Wait for automatic unlock; if it must be restored sooner, Admin resets the password on **Users** |
| EXP-08 | Suspected private key compromise | Revoke immediately per SOP-04 (key-compromise reason) + publish the CRL, report per the security incident process, pull audit logs for evidence afterwards (SOP-11) |

## 8. Records & Retention

| Record | Form | Frequency | Location / retention |
|--------|------|-----------|----------------------|
| Daily routine results | Approval/request queue cleared status | Daily | System audit log (automatic) |
| Expiring-certificate follow-up tracker | [Ticket/Excel] | Weekly | [Shared folder] / ≥ [12 months] |
| Audit log archive | CSV | Monthly | [Archive path] / ≥ [12 months] |
| Quarterly permission & baseline review | Checklist | Quarterly | [Compliance archive] |
| This SOP's revisions | Document revision history | Per revision | Section 9 of this document |

## 9. Revision History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| v1.0 | 2026-10-19 | First release | [ ] |

## Appendix A: Glossary

| Term | Meaning |
|------|---------|
| BAU | Business As Usual — routine day-to-day operations |
| AD CS | Active Directory Certificate Services (Windows enterprise CA) |
| CSR | Certificate Signing Request — public key plus request details; the private key never leaves the machine |
| PFX | Password-protected certificate container that includes the private key |
| SAN | Subject Alternative Name (multiple domains/IPs) |
| EKU | Extended Key Usage (e.g. Server Authentication, Client Authentication) |
| CRL / Delta CRL | Certificate Revocation List / incremental revocation list |
| CDP / AIA | CRL Distribution Point / Authority Information Access — where revocation and chain info are published |
| CertificateHold | The only revocation reason that can be undone (a temporary hold) |
| ICertAdmin2 / ICertView2 | COM interfaces CA-Manager's backend uses to administer/query AD CS |

## Appendix B: Web actions ↔ certutil equivalents

| Web action | Equivalent command |
|------------|--------------------|
| Certificate list/filter | `certutil -view -restrict "..."` |
| Revoke | `certutil -revoke <serial> <reason>` |
| Issue pending | `certutil -resubmit <RequestId>` |
| Deny | `certutil -deny <RequestId>` |
| Publish CRL | `certutil -CRL` / `certutil -CRL delta` |
| Submit CSR | `certreq -submit -attrib "CertificateTemplate:X"` |
| Enable/disable templates | CA properties → policy module Templates list |

## Appendix C: Related documents

- [Deployment Guide](deploy.md)
- [Operations Manual](operations.md) (roles & permissions, approval mechanism, service maintenance details)
