# Security, Tenant Isolation and Permissions

Status: **Phase 1 proposal**. Nothing is implemented yet.

## 1. Authentication

| Method | Users | Notes |
|---|---|---|
| Phone + SMS OTP | Patients (primary), staff (optional) | 6 digits, hashed, 5 min TTL, 5 attempts, resend cooldown, per-phone and per-IP limits. Provider is open (OD-09) |
| Telegram login | Patients (additional) | Login Widget hash verified server-side |
| Email + password (+ TOTP) | Staff, platform admins | Identity hasher + lockout. **TOTP required** for ClinicOwner, FinanceManager, PlatformAdmin (proposed) |

**Sessions.** An Identity cookie (`__Host-`, HttpOnly, Secure, SameSite=Lax) backed by a server-side
`sessions` table, so sessions can be revoked: logout everywhere, suspension, role removal. Antiforgery
is required on writes. The SPA and API are same-site, so CORS stays closed. Tokens are never kept in
localStorage.

**Account linking.** A new identity is attached only when the user is signed in **and** proves
control of it (OTP to the new phone, or a Telegram login hash). Accounts are never merged on matching
names, emails or phones. A conflicting identity leads to a support-handled request with an audit row.

**Recovery.** Patients recover via phone OTP. Staff get a single-use, time-limited email link and a
notification to the old channel. 2FA removal goes through support and is audited.

## 2. Authorization model

The scope is computed server-side per request from the DB: platform grants (explicit), active
memberships `{clinicId, role, Clinic | Branches[]}`, and whether a patient profile exists.

1. **Load, then authorize.** Fetch the resource with its clinic/branch, then check
   `HasPermission(user, perm, clinicId, branchId)`. Client ids are lookup keys only.
2. **Pre-filtered lists.** Queries join to the caller's accessible branch set.
3. Cross-tenant access returns **404**. In-tenant access without permission returns **403** (OD-14).
4. Suspension (user, clinic, branch) disables memberships and revokes sessions.
5. **Platform admins get no implicit tenant data access.** `platform.tenant-data.read` plus a reason
   is required, and each access is audited.
6. The role→permission map is code-defined and covered by a snapshot test. Nobody can grant
   permissions above their own.

### 2.1 Permission matrix (❓ = still open; "decided" = confirmed by the business owner on 2026-10-18)

C = clinic-wide, B = assigned branches, O = own only, — = denied.

| Capability | Owner | ClinicAdmin | Receptionist | Doctor | FinanceMgr | Patient | Guest | PlatformAdmin |
|---|---|---|---|---|---|---|---|---|
| Browse approved clinics/prices | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ | ✓ |
| Create appointment request | — | — | B (walk-in/phone, decided) | — | — | O | sign-in required | — |
| View/cancel own appointments, review | — | — | — | — | — | O | — | — |
| clinic.profile.manage, branch.manage | C | C | — | — | — | — | — | — |
| staff.manage | C (incl. owners) | C/B, not Owner/Admin roles | — | — | — | — | — | — |
| catalog.manage | C | C/B | — | — | — | — | — | — |
| schedule.manage | C | C/B | B (decided) | own time off ❓ | — | — | — | — |
| appointment.view | C | C/B | B | O (assigned) | B ❓ | — | — | break-glass |
| appointment.triage (confirm+assign/reject) | C | C/B | B | — | — | — | — | — |
| appointment.cancel (clinic side) | C | C/B | B | — | — | — | — | — |
| appointment.outcome (Completed/NoShow) | C | C/B | B (decided) | O (decided) | — | — | — | — |
| payment.record | C | ❓ | B (decided; refunds excluded) | — | C/B | — | — | — |
| payment.refund | C | — | — | — | C/B | — | — | — |
| finance.report.view | C | ❓ | — | — | C/B | — | — | platform.finance.settle |
| finance.settlement.view | C | — | — | — | C | — | — | platform.finance.settle |
| Platform perms (verify, suspend, reports, settle, tenant-data.read, audit.view) | — | — | — | — | — | — | — | each explicit |

Doctors see only the appointments assigned to them, plus the patient contact data the visit needs.

## 3. Tenant consistency in the DB

Every tenant table carries `clinic_id`. Children reference parents with composite FKs
`(clinic_id, x_id)`. The appointment has composite FKs to branch, branch_service, doctor_branch and
doctor_service, so "doctor works here and provides this service" is a DB invariant. RLS is planned as
defense in depth (DATABASE.md §4).

## 4. Abuse controls

Rate limits: OTP send 3/15 min per phone and 10/15 min per IP. OTP verify: 5 attempts per challenge.
Login is limited per account and per IP. Booking: 5/hour per patient. Search is limited per IP. Max
Pending requests per patient: 3 (OD-07). Idempotency keys on booking, payment and refund. Body size
limits. Pagination with a max of 100 per page.

## 5. Sensitive data

- **Never logged**: phones (logged masked only), names, OTPs, tokens, cookies, passwords, the bot
  token, full payment references. A redaction enricher plus a test enforce this.
- Telegram messages contain clinic, branch, service, time, status and a link. No patient phone in
  staff messages unless approved (OD-10).
- Errors are ProblemDetails with a correlation id. No stack traces or SQL outside Development.
- Stored personal data: name, phone, Telegram chat id, appointments, reviews. **No medical data and no
  free-text complaint field in v1** (OD-06).
- **Legal, verify before production:** Uzbekistan's Law "On Personal Data" (ZRU-547, 2019, as amended)
  requires citizens' personal data to be processed on servers **physically in Uzbekistan** and requires
  registration in the state personal-data register. Healthcare-specific rules may also apply. Hosting
  (OD-12) depends on legal advice. This document is not legal advice.

## 6. Secrets

Secrets come from env vars or Docker secrets. The Data Protection key ring is persisted. CI uses
masked/protected GitLab variables, and secret detection runs in CI. `.env.example` holds placeholders only.

## 7. Tests

`TESTING.md` §2. Each negative test substitutes ids directly in HTTP requests.
