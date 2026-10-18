# Decisions, Assumptions and Open Questions

## 1. Architecture decision records (all Proposed)

| Id | Decision | Rationale |
|---|---|---|
| ADR-001 | Modular monolith: API + Worker + one PostgreSQL | Booking, outbox and ledger need same-transaction atomicity |
| ADR-002 | One EF Core DbContext, a PostgreSQL schema per module | Cross-module transactions without distributed transactions |
| ADR-003 | Cookie sessions backed by a server-side session table | Revocation. No tokens in XSS-readable storage |
| ADR-004 | Per-branch row lock + **GiST exclusion constraint on doctor time blocks** (depends on OD-01) | The DB guarantees no doctor double-booking |
| ADR-005 | READ COMMITTED + explicit locks + constraints. Retry ≤ 3 (jittered) on 40001/40P01 | Constraints are the backstop. SERIALIZABLE adds retry noise without extra guarantees here |
| ADR-006 | Deadline checked in the write predicate with DB `now()`. Sweeper for side effects. Sweep-on-write before booking | Correct even if the Worker is down. The exclusion predicate can't use now() |
| ADR-007 | Double-entry append-only ledger. UNIQUE source_event_key | Reversal corrections, exact reconciliation, idempotency |
| ADR-008 | Outbox polled with `FOR UPDATE SKIP LOCKED`, no broker | No extra technology needed |
| ADR-009 | Integration tests on Testcontainers PostgreSQL only | Locks and exclusion constraints don't exist in InMemory/SQLite |
| ADR-010 | UUID v7 keys | Not enumerable, index-friendly |
| ADR-011 | Stack versions pinned (see §1.1) | Central package management + NuGet/npm lock files; versions are chosen as latest stable at Phase 2 |
| ADR-012 | Microsoft.Testing.Platform runner with xUnit v3 (`global.json` → `test.runner`) | .NET 10 SDK no longer runs VSTest for MTP-based frameworks |
| ADR-013 | No EF Core `EnableRetryOnFailure` | Execution strategies reject explicit transactions and replay lock-taking work; retries are explicit (ADR-005) |
| ADR-014 | Migrations are applied by a one-shot EF bundle container (`migrate`) as the schema-owner role; API/Worker never migrate | Least privilege; deterministic startup; readiness reports pending migrations |
| ADR-015 | Built-in JSON console logging (no Serilog) and built-in validation/OpenAPI | Avoids extra dependencies until a requirement needs them |
| ADR-016 | **Auth = opaque cookie + server-side sessions + Identity `PasswordHasher` only** (BE1a, 2026-10-18). The `__Host-oqtish` cookie (HttpOnly, Secure, SameSite=Lax) carries a 256-bit random token; `identity.sessions` stores only its SHA-256. A custom authentication handler validates the session in the DB on **every** request (revoked / expired / 14-day idle / security-stamp mismatch / user not Active → 401). Passwords use ASP.NET Core Identity's `PasswordHasher<T>` (PBKDF2) from `Microsoft.Extensions.Identity.Core`; the full Identity user store (`AspNetUsers`, `UserManager`, Identity cookie) is **not** used | Revocation, logout-all and suspension take effect on the next request (no stale encrypted tickets). The Identity store's schema (one email/username per user, claims/roles/tokens tables) does not fit phone-first + Telegram identities with `UNIQUE(kind, identifier)` and per-clinic memberships; we would use 10% of it and fight the rest. Data Protection is used only to encrypt TOTP secrets, with its key ring in `identity.data_protection_keys` |
| ADR-017 | TOTP-enabled accounts must present a TOTP code on **every** sign-in method (password, phone OTP, Telegram) via an optional `totpCode` field; privileged permissions (ClinicOwner, FinanceManager, platform) require TOTP *enabled* (SECURITY.md §8). Enabling TOTP revokes the user's other sessions | One consistent rule; no "weaker" sign-in path bypasses the second factor, and every live session of a TOTP account was established with it |
| ADR-018 | OTP SMS is sent directly after commit (not via the outbox) | The code has a 5-minute TTL and the user is waiting; the outbox poll adds latency. A send failure is logged without the phone and the 202 response is unchanged (no enumeration). ARCHITECTURE.md §2's "OTP via outbox" is superseded |

### 1.1 Pinned versions (Phase 2, 2026-10-18)

| Component | Version |
|---|---|
| .NET SDK / runtime | 10.0.401 / 10.0.12 |
| Microsoft.* packages (EF Core Design, OpenApi, HealthChecks EF, Hosting, Mvc.Testing) | 10.0.12 (matches installed runtime) |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
| Microsoft.Extensions.Identity.Core, Microsoft.AspNetCore.DataProtection.EntityFrameworkCore, Microsoft.EntityFrameworkCore (Application) | 10.0.12 (BE1a) |
| System.Security.Cryptography.Xml (transitive pin via Data Protection) | 10.0.13 — 10.0.12 has CVE-2026-96958 / GHSA-r955-8q5j-3h83 (NuGet audit fails the build) |
| EFCore.NamingConventions (snake_case) | 10.0.1 |
| Swashbuckle.AspNetCore.SwaggerUI | 10.2.3 |
| xunit.v3 | 4.0.1 |
| Testcontainers.PostgreSql | 4.15.0 |
| dotnet-ef (local tool) | 10.0.12 |
| PostgreSQL image | postgres:18-alpine |
| Node.js | 24.21.0 LTS (local), node:24-alpine (CI/images) |
| React / Vite / TypeScript | 19.2 / 8.3 / 6.0 (exact versions in `web/package-lock.json`) |
| Vitest / Testing Library React / jsdom | 5.0.5 / 16.3.3 / 30.1.3 |
| oxlint | 1.81 |
| nginx image | nginxinc/nginx-unprivileged:1.28-alpine |

## 2. Assumptions (please correct)

- A1. Uzbekistan only, UZS only, default time zone Asia/Tashkent.
- A2. Patients must sign in (phone OTP / Telegram) before submitting a request. Guests browse only.
- A3. Capacity is limited by doctors only, not chairs or rooms.
- A4. Start times sit on a branch-configured grid (default 15 min). Overlap uses the real [start, end).
- A5. The 30-minute expiry runs from creation in server time, including outside working hours.
- A6. Platform staff verify clinics manually before they are publicly listed.
- A7. In v1, staff record payments (cash/terminal/transfer). The platform processes no card payments.
- A8. A doctor record belongs to one clinic.
- A9. A review requires a Completed appointment. One per appointment, within 30 days, moderated.
- A10. UI in Uzbek (Latin) and Russian.
- A11. Production hosting is in Uzbekistan (data localisation). Legal confirmation is pending.

## 3. Open business decisions

| Id | Question | Needed by | Recommendation |
|---|---|---|---|
| **OD-01** ✅ decided 2026-10-18: yes | At booking, may the system **provisionally hold a specific eligible doctor's time** (hidden from the patient, reassignable at confirm)? | Phase 4 | **Yes.** Counting capacity without a doctor is an assignment problem (different service sets and durations). It's hard to get both correct and fast, and no DB constraint can enforce it |
| OD-02 | Are chairs/rooms limited resources? | Phase 4 | No in v1 |
| OD-03 | Should expiry pause outside working hours? Minimum booking lead time? | Phase 4 | Keep 30 min literal. Add a 60 min minimum lead time |
| OD-04 | Patient cancellation deadline for Confirmed | Phase 4 | Until 2 h before start |
| OD-05 | Reschedule flow | Phase 4/6 | Clinic-initiated move + notification. Patient: cancel + rebook |
| OD-06 | Any patient free-text field? | Phase 3 | None (avoids collecting medical data) |
| OD-07 | Max Pending requests per patient | Phase 4 | 3 |
| OD-08 ✅ decided 2026-10-18: yes | Can receptionists book for walk-in/phone patients without an account? | Phase 4 | Likely needed. Requires a clinic-managed contact + consent rule |
| OD-09 ⏸ deferred 2026-10-18: neutral adapter + Development-only fake | SMS provider (e.g. Eskiz, Play Mobile) and budget | Phase 3 | Business choice. The adapter is neutral |
| OD-10 | Telegram: personal chats only, or staff groups? Patient phone in staff messages? | Phase 6 | Personal only. No phone |
| OD-11 | Fallback when Telegram is not linked or fails | Phase 6 | SMS for confirm/reject/clinic-cancel. The status page always works |
| OD-12 | Hosting provider/location | Before prod | Uzbek data centre/cloud + legal sign-off |
| OD-13 | Enable RLS in v1? | Phase 8 | Yes, on the 5 sensitive tables |
| OD-14 | 404 vs 403 across tenants | Phase 3 | 404 cross-tenant, 403 in-tenant |
| OD-15 | UI languages | Phase 7 | uz-Latn + ru |
| OD-16 | Distance search | Phase 7 | Haversine in SQL, no PostGIS, no map SDK |
| OD-17 | Availability search horizon | Phase 7 | Next 7 days, server-computed |
| OD-18 | Retention and self-deletion | Phase 8 | Legal input. Anonymise rather than delete where finance references exist |
| OD-19 (partly decided 2026-10-18) | Matrix cells in SECURITY.md. Decided: Receptionist may record payments (not refunds), book walk-ins, manage branch doctor schedules, mark Completed/NoShow; Doctor may mark own appointments Completed/NoShow. Still open: ClinicAdmin finance rights, FinanceManager appointment visibility, Doctor own time off | Phase 3 | Admin: no finance by default; FinanceManager: read-only appointment+amount view; Doctor: request time off, approved by admin |
| OD-F1…F11 | Accounting rules | Phase 5 | FINANCIAL_RULES.md §10 |

## 4. Decisions taken under delegation (2026-10-18)

On 2026-10-18 the business owner delegated all remaining open decisions to the implementer ("decide
them yourself in the best way"). Each is chosen to be safe and reversible. Every item is configurable
or isolated so the owner can change it later without data migration pain. **Revisit before launch.**

| Id | Decision |
|---|---|
| OD-02 | Capacity = doctors only. No chair/room resources in v1. |
| OD-03 | 30-minute expiry counts in real time, 24/7 (`Booking:PendingTtlMinutes` = 30). Minimum lead time **60 min** (`Booking:MinLeadMinutes`), maximum horizon **30 days** (`Booking:HorizonDays`). Slot grid **15 min** (`Booking:SlotStepMinutes`). |
| OD-04 | A patient can cancel a Pending request at any time, and a Confirmed appointment until **2 h before start** (`Booking:PatientCancelCutoffMinutes` = 120). After that only the clinic can cancel. |
| OD-05 | Reschedule is clinic-initiated only: it moves a Confirmed appointment (same service) to a new time/doctor atomically and notifies the patient. Patient-side reschedule = cancel + new request. |
| OD-06 | No free-text field from patients on appointments (avoids medical data). Reviews have text (max 1000 chars) and are moderated. |
| OD-07 | Max **3** active (Pending) requests per patient (`Booking:MaxPendingPerPatient`). |
| OD-08 | Walk-in / phone booking by staff creates a **Confirmed** appointment directly (doctor chosen by staff) with contact name + phone and **no patient account** (`patient_user_id` NULL). No automatic linking to accounts by phone. |
| OD-09 | SMS: provider-neutral `ISmsSender`. `Fake` (Development/tests only — startup refuses it in Production) and `Eskiz` adapter (Eskiz.uz HTTP API). The Eskiz adapter is **not verified against the live service** until credentials exist. |
| OD-10 | Telegram: personal chats only. Staff messages contain clinic/branch/service/time and request number, **never** patient phone. Patient messages contain clinic/branch/service/time/status. |
| OD-11 | Fallback when Telegram is unavailable or not linked: SMS for Confirmed, Rejected, CancelledByClinic, Rescheduled; everything is always visible in "My appointments". Reminders: Telegram only (24 h and 2 h before). |
| OD-12 | Hosting: must be in Uzbekistan (localisation law). Deployment docs assume a generic Linux host with Docker; provider choice and legal sign-off remain with the owner. |
| OD-13 | RLS: not enabled in v1. Defence in depth comes from composite tenant FKs + least-privilege roles + the authorization test suite. Recorded as a hardening item. |
| OD-14 | Cross-tenant access → 404; in-tenant permission missing → 403. |
| OD-15 | UI languages: uz (Latin), ru, en — decided by owner. |
| OD-16 | Distance: branch latitude/longitude + Haversine in SQL; no PostGIS, no map SDK (map = external link). |
| OD-17 | "Search by availability": filter "has a free slot within the next 7 days" for a chosen service. |
| OD-18 | Retention: OTP challenges deleted after 24 h; revoked/expired sessions after 30 days; delivered outbox rows after 30 days; audit log and ledger are kept (no deletion). Patient self-deletion = anonymisation of profile and contact fields (appointments/ledger rows stay, de-identified). |
| OD-19 | ClinicAdmin: no finance rights by default. FinanceManager: read-only appointment view (branch scope) for payment context. Doctor: may add/remove **own** time off (not overlapping active appointments); may mark own appointments Completed/NoShow. |
| OD-20 | Clinic onboarding: any signed-in user may register a clinic; it starts `Pending` verification with the user as ClinicOwner; it is hidden from public search until a platform admin approves it. |
| OD-21 | Staff invitations are addressed to a **phone number**; the invitee accepts after signing in with that verified phone. Staff may also use email+password (+TOTP) once set. |
| OD-22 | Staff password recovery: one-time code to the staff member's verified phone; generic responses (no account enumeration). |
| OD-23 | CSRF: cookie `SameSite=Lax` + required custom header `X-OqTish-Client` on every non-GET API request (browsers cannot send it cross-site without a CORS preflight, and CORS is closed). |
| OD-24 | Abuse reports: patients can report a clinic or a review; platform admins resolve them. |
| OD-F1 | Commission basis is **configured per clinic contract** by a platform admin with a contract reference. Supported bases: `CollectedPayment` (% of net collected), `CompletedService` (% of charged amount), `ConfirmedAppointment` (fixed fee), `NewPatient` (fixed fee for a patient's first completed visit at that clinic). **No default rule exists**; without an active rule commission is zero and reports say so. The development seed contains one clearly labelled demo rule. |
| OD-F2 | Applicable rule = the rule whose effective range contains the **basis event time** (payment time / completion time / confirmation time). |
| OD-F3 | Refunds reverse commission **proportionally** for `CollectedPayment` and `CompletedService`; fixed-fee bases are not reversed by refunds. Reversals post in the refund's period. |
| OD-F4 | Clinic revenue recognised at ServiceCompletion (accrual); cash recorded at payment time. |
| OD-F5 | 2 decimal places, `MidpointRounding.AwayFromZero`, rounded per ledger line. |
| OD-F6 | A month is locked when its settlement is Finalized; later postings for that clinic land in the current open period. |
| OD-F7 | The platform collects **no** patient money in v1 (all payments are recorded by clinics). Settlement = clinic owes platform its commission. |
| OD-F8 | The quoted price is not binding. At completion staff enter the charged amount (pre-filled with the quoted price), any value ≥ 0. |
| OD-F9 | Amounts are gross; VAT/tax invoicing is out of scope for v1 (documented gap, needs an accountant). |
| OD-F10 | Settlements are monthly per clinic. Payment terms are informational text on the settlement. |
| OD-F11 | No no-show fee in v1. |

## 5. Decisions taken in BE1a (identity, common infrastructure) — 2026-10-18

| Id | Decision |
|---|---|
| BE1a-1 | `Security:OtpPepper` keys every HMAC (OTP codes, phone/e-mail/IP lookups, invitation phone hash) with purpose prefixes. Outside Development it is required (≥ 32 chars, not the public dev value) or startup fails. Rotating it invalidates outstanding OTPs and pending invitations only. |
| BE1a-2 | OTP limits are per **identifier** (the phone; for password reset the e-mail) across purposes: one code per 60 s, 3 per 15 min, plus 10 per 15 min per client IP, enforced under a transaction advisory lock. Every verify submission counts as an attempt (5 per challenge). |
| BE1a-3 | Password reset for an unknown e-mail creates a **decoy challenge** (no SMS, never verifiable) so responses, rate limits and attempt counting are identical to a real account. |
| BE1a-4 | First password (`POST /me/password`) requires a verified phone and an `email` (new optional field) used as the login name. The e-mail is not verified (no e-mail channel in v1); it is never used for recovery, which goes through the verified phone (OD-22). |
| BE1a-5 | Account linking never merges: a phone/Telegram id owned by another account → 409 `auth.identity_in_use` plus a committed `identity.link_conflict` audit row for support. `identity.account_link_requests` (DATABASE.md proposal) is therefore not created. |
| BE1a-6 | Telegram unlink is refused (`auth.last_identity`) when Telegram is the account's only sign-in method. |
| BE1a-7 | Self-deletion (OD-18) is blocked while the user has a non-removed membership or an active platform grant; otherwise it anonymises in one transaction and calls every registered `IAccountDeletionContributor` (later modules de-identify appointments etc.). |
| BE1a-8 | `Branches`-scoped memberships never hold the clinic-wide permissions `clinic.profile.manage`, `audit.view`, `finance.settlement.view`; `AccessScope.CheckClinicWide` exists for actions about the whole clinic (e.g. creating a branch). Pending/Rejected clinics keep their memberships active (owners must be able to finish onboarding); only Suspended clinics disable them. |
| BE1a-9 | On plain-HTTP Development the cookie is named `oqtish-dev` (browsers reject `__Host-` cookies without Secure); over HTTPS, or when `Auth:Session:RequireSecureCookie` is true (default), it is `__Host-oqtish`. |
| BE1a-10 | `auth.totp_invalid` is 401 on sign-in endpoints but **400** on `/me/totp/*`, and a wrong current password is 400 `auth.current_password_invalid`, so the SPA never mistakes a signed-in user's typo for a lost session. |

### 5.1 Decisions taken in BE1b (clinic console, catalogue, scheduling, admin) — 2026-10-18

| Id | Decision |
|---|---|
| BE1b-1 | Service categories are a **fixed catalogue of keys** (`diagnostics`, `hygiene`, `therapy`, `surgery`, `implants`, `prosthetics`, `orthodontics`, `pediatric`, `whitening`, `periodontics`, `other`; Application `ServiceCategories`), not free text, so the public category filter works across clinics. The UI localises by key. |
| BE1b-2 | A service belongs to the whole clinic: creating/editing a service needs `catalog.manage` **clinic-wide**; a branch offer (price/duration) needs `catalog.manage` on that branch. Creating a branch needs `branch.manage` clinic-wide (BE1a-8). |
| BE1b-3 | Anti-escalation: the role rule is `Permissions.RolesManageableBy` (SECURITY.md §8: owners manage every role, ClinicAdmin manages Receptionist/Doctor/FinanceManager — even though those roles hold payment permissions ClinicAdmin lacks, as §8 explicitly allows). "Nobody grants above their own permissions" is enforced as **scope containment** (a Branches-scoped manager only sees/changes/grants Branches-scoped memberships and invitations inside its own branches) plus **no change to one's own membership**. Leaving (DELETE own membership) is allowed. |
| BE1b-4 | Staff changes of one clinic are serialised by a transaction advisory lock `staff:{clinicId}`; the `staff.last_owner` guard (an active owner must remain) applies to demotion and removal. Removal sets status `Removed` (effective on the next request, no session revocation needed). |
| BE1b-5 | Invitations: a new invitation for the same phone in the same clinic revokes the open one; **no SMS is sent in v1** (the invitee finds it under `GET /me/invitations` after signing in with that phone; notifications are BE4). Accept requires a **verified** phone whose HMAC matches; otherwise 404 (no enumeration). Expiry is checked with DB time in the accepting UPDATE → 422 `staff.invitation_expired`. Accepting reactivates a previously removed membership (UNIQUE(clinic_id, user_id)). |
| BE1b-6 | Linking a doctor record to an account (`userId`) grants Doctor-own access, so it additionally needs `staff.manage`; the user must hold a non-removed membership in the clinic; one doctor per user per clinic (partial unique index). `DoctorIdentityResolver` (doctors.user_id) replaces the BE1a placeholder. |
| BE1b-7 | Working hours: one weekly template per doctor (no effective dates in v1). PUT replaces only the entries of branches on which the caller holds `schedule.manage`; other branches' entries are kept. Intervals of a doctor never overlap on a weekday **across branches** (handler check under an advisory lock + exclusion constraint on a custom `time` range type). Unlinking a branch cascades its hours. Hours are not forced inside branch opening hours (availability in BE2 intersects them). |
| BE1b-8 | "Read: membership" lists are filtered in SQL to the caller's branches for Branches-scoped members (branches, offers, doctors, working hours, dashboard). |
| BE1b-9 | Public listing = `Approved` clinics with ≥ 1 active branch; only active offers of active services. Rating = `tenancy.clinics.rating_average` / `review_count` (null/0 until BE2 moderation maintains them). `availableWithinDays` is validated and accepted but applied after BE2; `nextFreeSlot` is null. Default sort: distance with coordinates, otherwise rating (nulls last) → name. |
| BE1b-10 | Clinic onboarding abuse guard: at most **3 Pending** registrations per user (`clinic.registration_limit`). |
| BE1b-11 | Clinic status machine: approve = Pending/Rejected → Approved; reject = Pending → Rejected (reason required); suspend = Approved → Suspended (reason required); reinstate = Suspended → Approved. Other transitions → 409 `admin.invalid_status_transition`. Approve/reject need `platform.clinic.verify`, suspend/reinstate `platform.account.suspend`. |
| BE1b-12 | User suspension rotates the security stamp **and** revokes all sessions; a platform admin cannot suspend their own account. |
| BE1b-13 | `create-platform-admin` CLI (every environment): password from `OQTISH_ADMIN_PASSWORD` or stdin only (an argv `--password` is refused); policy ≥ 12 chars, ≥ 3 of lower/upper/digit/symbol, ≥ 6 distinct chars, must not contain the e-mail name. Idempotent: an existing account keeps its password; missing phone identity (marked verified — the operator vouches for it) and grants are added. A phone/e-mail of another account is refused, never merged. Audited as `platform.admin.bootstrapped` (actor System). TOTP must still be enabled before the grants take effect. |
| BE1b-14 | Demo staff logins with documented passwords are created only when `Seed:DemoStaffAccounts=true` (set in appsettings.Development.json only), so a Staging seed never creates guessable accounts by default. |
| BE1b-15 | Branch time zones: allow-list `Asia/Tashkent` (default), `Asia/Samarkand` (Uzbekistan only, A1). |
| BE1b-16 | Dashboards are extensible: `IClinicDashboardContributor` (BE2 appointment counts, BE3 collected sum) and `IAdminDashboardContributor` (appointments today, open reports) are scoped DI services; without them the counts are 0. |
| BE1b-17 | Test infrastructure: the Testcontainers PostgreSQL runs with `max_connections=300` and every `ApiFactory` clears its database's connection pool on dispose (one database per test). |

### 5.2 Decisions taken in BE2 (booking, reviews, reports) — 2026-10-18

| Id | Decision |
|---|---|
| BE2-1 | **Time blocks** keep `starts_at`/`ends_at` columns (Domain stays free of Npgsql range types); the migration adds `period tstzrange` as a STORED generated column (`'[)'`) and the exclusion constraint `EXCLUDE USING gist (doctor_id WITH =, period WITH &&) WHERE (is_active)` on it. Each appointment owns at most one block row (`appointment_id` UNIQUE): confirm/reschedule move it, release deactivates it. TimeOff blocks have no branch (the doctor is away everywhere). |
| BE2-2 | **Database time everywhere in booking.** Handlers read `clock_timestamp()` after taking their locks (`IDbClock`) for deadlines, lead time, horizon, cancel cutoff, outcome times and the review window; conditional UPDATEs compare `expires_at` with **`statement_timestamp()`**, not `now()` — `now()` is frozen at transaction start and would accept a confirm that waited on a lock past the deadline. |
| BE2-3 | **Locking strategy (ADR-004/005/006 as implemented).** READ COMMITTED; lock order *idempotency key row → branch row (`tenancy.branch_booking_locks FOR UPDATE`) → per-patient advisory lock (create only) → appointment rows (`FOR UPDATE`, id order) → block rows*. The Worker sweeper takes no branch lock and claims with `FOR UPDATE SKIP LOCKED`. Every write is a conditional UPDATE on the expected status after the row lock; the exclusion constraint is the final guarantee across branches and paths without the branch lock (time off, walk-ins at another branch). Only 40001/40P01 are retried (≤ 3 attempts, `ITransactionRunner`); exhaustion → 409 `common.concurrency`. |
| BE2-4 | **Sweep-on-write scope:** before inserting or moving a block, a write expires overdue Pending requests of its branch **and** overdue holds of the doctors involved (any branch), with plain `FOR UPDATE` (waits for, never skips, a concurrent transition). Confirm excludes the request it confirms — found by test B7: otherwise the request's own deadline passing mid-transaction made the sweep expire it and the confirm insert a duplicate block. |
| BE2-5 | **Provisional hold (OD-01):** the appointment row is inserted first, then one Hold per candidate is tried under a SAVEPOINT (23P01 → roll back to the savepoint, next candidate). Candidates = eligible doctors whose working hours cover the interval and who have no time off then; ordered least flexible (fewest services) → least booked that day → id. Greedy, not optimal (B5 documents a case it cannot solve); staff can reassign at confirm. |
| BE2-6 | **Error split:** no eligible doctor working then (hours, time off, branch opening hours) → 422 `booking.outside_hours`; doctors work but all are busy → 409 `booking.slot_unavailable`. Branch opening hours restrict availability only when the branch has opening hours configured. A start off the 15-minute grid → 400 `common.validation`. Availability `date` outside today … today + 30 → 400. |
| BE2-7 | **Staff actions** (confirm, reschedule, walk-in) require an active doctor linked to the branch who provides the service and is free; working hours are **not** required (staff decide; `eligible-doctors` reports `withinWorkingHours`). Reschedule keeps the duration and needs a future start; walk-ins may start up to 60 min in the past (patient already in the chair) and get no lead time. |
| BE2-8 | **Reason codes** = union of the brief and the UI (FRONTEND.md F5): reject `NoDoctorAvailable, TimeUnavailable, ServiceUnavailable, PatientUnreachable, Other`; clinic cancel `DoctorUnavailable, ClinicClosed, PatientRequest, ClinicEmergency, Other`. History uses `PatientCancelled`, `AccountDeleted`, `Rescheduled`. |
| BE2-9 | Completed and NoShow **release the block** (invariant B18: every active block ↔ Pending (Hold) / Confirmed (Appointment) / TimeOff). Staff cancel only Confirmed appointments (Pending → reject); staff `canCancel` is true only for Confirmed with `appointment.cancel`. |
| BE2-10 | **Completion hook for BE3:** `IAppointmentCompletedHandler.OnCompletedAsync` runs inside the completion transaction after the status/charged amount were written; a failure result rolls the completion back. Default: no-op (`TryAddScoped`); BE3 replaces it. |
| BE2-11 | **Notifications:** typed intents in the outbox (`appointment.<event>.<audience>`, dedup `appt:{id}:{event}:{audience}`; reschedule notices add the start time). Reminders 24 h / 2 h before start, dedup `appt:{id}:reminder:{kind}:{start}`, enqueued on confirm/reschedule only when still in the future, and set to `Cancelled` by Booking when the appointment leaves Confirmed or moves. BE4's dispatcher must still re-check status and start time before sending. Walk-ins (no account) get no notices. Payloads carry no phone or name. |
| BE2-12 | Removing a doctor's branch/service link that appointments or blocks reference → 409 `schedule.link_in_use` (the composite FKs are RESTRICT, so history keeps its integrity); deactivate the doctor instead. |
| BE2-13 | `Idempotency-Key` is optional (recommended) on `POST /appointments` and walk-ins; the key is scoped per user and hashed with the request (walk-ins include the route clinic). |
| BE2-14 | Patient booking rate limit: **10 requests per hour per session** (`RateLimiting:BookingPermitPerHour`; the limiter runs before authentication, so the bucket is keyed by a hash of the session cookie, else the IP). Deviation from SECURITY.md §4 (5/hour): failed attempts (409 slot taken) count too, and the 3-Pending cap already bounds abuse. |
| BE2-15 | **Reads present overdue Pending as Expired**; list filters `status=Pending/Expired` follow the same rule; the sweeper only makes side effects (notices, history, freed time) prompt. |
| BE2-16 | **Reviews:** author label "First L." (from the patient profile; "Bemor" when unknown/anonymised). Moderation Pending/Hidden ⇄ Published, same status → 409 `admin.invalid_status_transition` (existing code reused). The clinic rating is recomputed from Published reviews under a `FOR UPDATE` lock on the clinic row (a concurrency test proves two simultaneous publications keep the exact average). |
| BE2-17 | **Abuse reports:** target must be public (Approved clinic / Published review); one open report per (reporter, target) — a repeat returns the existing one (partial unique index). |
| BE2-18 | **Break-glass:** permission check first (403), then the percent-decoded `X-Access-Reason` must be 15–300 characters without control characters (400), then the clinic (404). The audit row stores the decoded reason, filters and row count. Only appointments are exposed in v1. |
| BE2-19 | **Search availability:** `availableWithinDays = N` keeps clinics with a free slot on local days today … today + N − 1; `nextFreeSlot` uses N or 7. Computed in memory from a constant number of queries (no cache, no cap — acceptable for v1 scale; revisit with many clinics). |
| BE2-20 | **Self-deletion** cancels the user's Pending and not-yet-started Confirmed appointments (reason `AccountDeleted`, blocks released, staff notified) and anonymises `contact_name`/`contact_phone` of all their appointments. |
| BE2-21 | **Dashboards:** clinic `pending` = requests waiting now (any date), `confirmed/completed/noShow` = appointments starting today; Doctor role counts only its own. Admin `appointmentsToday` = Pending (not overdue), Confirmed, Completed, NoShow starting today in Asia/Tashkent. |
| BE2-22 | **Tests:** the concurrency catalogue runs the real Application + Infrastructure DI graph without HTTP (`BookingHost`: real handlers, transactions, AccessService and PostgreSQL; the caller is set per DI scope) so 50-iteration loops stay fast; contracts and authorization are tested over HTTP with real sign-in. |

### 5.3 Decisions taken in BE3 (finance) — 2026-10-18

Implementation choices under the delegation (§4). They do not add contract terms; the commission terms remain
whatever a platform admin enters as a rule (OD-F1). **Revisit before launch.**

| Id | Decision |
|---|---|
| BE3-1 | **Account map and signs** (FINANCIAL_RULES §11.1): debit = +, credit = −; Transfer payments get their own account `ClinicCollectedTransfer` (§3 listed Cash/Card only). Clinic-level entries (settlement paid) have no branch and belong to no reporting day. |
| BE3-2 | **Refund semantics**: a refund first returns the patient's credit on the appointment (negative receivable, e.g. prepayment/overpayment); the rest is a **price reduction** of the delivered service (Dr ClinicServiceRevenue). So F2 ends with receivable 0, net revenue 400 000.00, and `outstanding` never says "the patient owes the refunded money". Refunds keep the original payment's method. |
| BE3-3 | **Commission reversal (OD-F3)** is cumulative-proportional: `round(original × Σreductions / basis) − Σalready reversed` (a 100 % refund reverses exactly the original line). CollectedPayment uses the cumulative refunds of that payment; CompletedService uses the cumulative price reductions (BE3-2) of the appointment — returning a patient's credit does not reduce it. The reversal keeps the original rule/rate snapshot. Fixed fees are never reversed (also not on cancellation after confirmation). |
| BE3-4 | **ConfirmedAppointment** fee is posted by a new in-transaction hook `IAppointmentConfirmedHandler` on the staff confirm path only; **walk-ins** (created Confirmed by staff, not platform-originated) get no fee. **NewPatient** = the first ServiceCompletion of a patient **account** at the clinic (walk-ins without an account never count); serialised by an advisory lock per (clinic, patient); a zero-charge first visit counts. |
| BE3-5 | **Rules affect only postings made after they exist** (ledger lines are immutable; nothing is recomputed retroactively). A rule may be created with a past `effectiveFrom`, but **cannot be ended in the past** (`effectiveTo ≥ now`, database time), so posted lines always lie inside their rule's range. |
| BE3-6 | **Period lock (OD-F6)**: postings dated into a month with a Finalized/Paid settlement are **redirected** to the first day of the next open month (not rejected). Postings hold the clinic period advisory lock in shared mode; settlement create/finalize hold it exclusively; finalize re-collects lines posted after the draft. A Finalized/Paid settlement cannot be voided (409 `finance.period_locked`). Settlements can only be created for months that have ended (Asia/Tashkent). |
| BE3-7 | **Idempotency-Key is required** for payment and refund POSTs (deviation from booking, where it is optional): money writes must be replay-safe. The key is per user (common store) and also UNIQUE per clinic on `payments` (another user's key → 422 `common.idempotency_mismatch`). |
| BE3-8 | **Failed payments**: (a) additive `status: 'Failed'` on record = a declined attempt stored with **no** ledger lines (F10); (b) `fail` on a recorded payment = reversal entries (F11, also used for entry mistakes). `fail` is refused while the payment has refunds. Overpayment is allowed (FRONTEND F8: the server decides — it accepts and shows credit). |
| BE3-9 | Payments allowed only for **Confirmed/Completed** appointments (422 `finance.appointment_not_payable`). Prepayments on Confirmed appointments are patient credit until completion. |
| BE3-10 | **Reports** come from the ledger, never the cache; `reconciled` compares the cache with the ledger per (branch, day) and is therefore false until the nightly job covered the month. Platform admins with `platform.finance.settle` may read clinic reports and settlements (SECURITY §2.1 row), not the payments list (patient contact data). Dashboard `collected` = **net** of refunds and failed reversals. |
| BE3-11 | **Nightly job**: Worker `FinanceSummaryService`, at startup and daily at `Finance:SummaryRunAtLocalTime` (02:00 Asia/Tashkent), window `Finance:SummaryWindowDays` (35). Mismatch → warning log (branch id + date only), overwrite, audit `finance.summary.mismatch`. |
| BE3-12 | **No outbox intents** for payments/refunds in v1: there is no recipient/message defined for BE4; audit rows cover traceability. BE4 may add `payment.*` intents inside the same transactions. |
| BE3-13 | Demo finance seed only with `Seed:DemoFinance=true` (appsettings.Development.json only): rule `DEMO-0001` (CollectedPayment 0.1000 from 2026-01-01) for the first demo clinic + demo payments. |
| BE3-14 | Settlement `paidAt` may not be more than one day in the future; payment terms are free informational text (OD-F10). Settlement lists are unpaginated (≤ 500), as the UI assumed. |

### 5.4 Decisions taken in BE4 (notifications, Telegram bot) — 2026-10-18

Implementation choices under the delegation (§4). **Revisit before launch.**

| Id | Decision |
|---|---|
| BE4-1 | **Dispatcher = claim + lease, not a long transaction.** One statement claims due rows (`Pending ∧ next_attempt_at ≤ now()` or `Processing ∧ locked_until < now()`) with `FOR UPDATE SKIP LOCKED`, sets Processing + claim token + lease (`Notifications:LeaseSeconds` 300) and increments `attempts`; provider calls run outside any transaction; the result is written with a conditional UPDATE on the claim token. A crashed run's rows are reclaimed after the lease. Delivery is **at-least-once**: a crash between a provider call and its `delivery_attempts` row can repeat that one send; the dedup key bounds it to one message per business event and per-recipient attempt rows stop repeats to recipients that already have a final outcome. |
| BE4-2 | **Retries:** max 6 attempts (`Notifications:MaxAttempts`), backoff after attempt 1–5 = 1 m, 5 m, 30 m, 2 h, 6 h (`BackoffSeconds`) ± 20 % jitter; a 429 `retry_after` longer than the backoff wins; short ones (≤ 5 s) are waited out inside the client once. Classification: timeout/network/5xx/401/404/409 transient; 403 and "chat not found" → chat link deactivated (`blocked`); other 400 permanent (Failed, no retry); unknown message type / bad payload → Failed. |
| BE4-3 | **Channels (OD-11):** Telegram when enabled and an **active** chat link exists; otherwise SMS **only** for Confirmed, Rejected, CancelledByClinic, Rescheduled; otherwise Skipped (`notify.no_channel`). For these four types a Telegram failure (transient or blocked) falls back to SMS **in the same attempt** (the patient may rarely get both); other types retry Telegram. Reminders and staff messages are Telegram only. |
| BE4-4 | **SMS goes to the appointment's contact phone** (the number entered when booking, or the walk-in contact), read at send time — payloads keep carrying no phone (OD-10). After account deletion the phone is anonymised → no SMS. |
| BE4-5 | **Walk-ins get SMS** for CancelledByClinic and Rescheduled only (supersedes the "no notices" part of BE2-11); creating a walk-in sends nothing (the patient is on the phone / in the chair). `BookingNotifier` now enqueues those two patient intents also when `patient_user_id` is NULL. |
| BE4-6 | **Staff recipients:** active memberships of the clinic (clinic not Suspended, user Active) whose role holds `appointment.triage` (Owner, Admin, Receptionist), Clinic scope or a branch grant for the appointment's branch, with an active chat link. TOTP-blocked owners still receive (the text has no personal data). Staff texts: clinic, branch, service, time, deadline, request number, console button — never patient phone or name. |
| BE4-7 | **Reminder re-check:** the dispatcher re-reads the appointment; not Confirmed or a different `starts_at` → Cancelled `reminder.stale`; start already passed (Worker was down) → Cancelled `reminder.too_late`. |
| BE4-8 | **Telegram client selection:** `Telegram:Client` = Real / Fake; empty → Fake in Development, Real elsewhere; Fake refused in Production (startup guard). Development captures Fake Telegram and Fake SMS sends in `notify.dev_messages` (only when the environment is Development) so `/api/dev/telegram` shows what the separate Worker process sent; `POST /api/dev/telegram/simulate` feeds bot commands locally without a real bot. |
| BE4-9 | **Modes:** `Telegram:Mode` Polling (default; the Worker deletes any webhook, long-polls getUpdates and persists the offset per bot id after each handled update — a crash re-handles at most one update) or Webhook (the API calls setWebhook on startup with `WebhookBaseUrl`; path segment derived from `WebhookSecret` = hex(SHA-256("oqtish-webhook-path:"+secret))[..32]; wrong path → 404, wrong secret header → 401; always 200 after authentication). Webhook mode requires a 32–256-char secret and an https base URL when enabled (startup guard). |
| BE4-10 | **Bot commands:** `/stop` **deactivates** the chat link (reason `stopped`) instead of deleting the Telegram identity (that would remove a sign-in method; `DELETE /me/telegram` still unlinks fully). `/start` without a token re-activates a stopped/blocked link, or links the chat of a user whose verified Telegram identity matches the sender (Mini App / Login Widget sign-in) — Telegram authenticates the sender. Group chats, channels and bot senders are ignored; per-chat flood limit 20/min (in-process). |
| BE4-11 | **Language:** stored on the chat link from Telegram `language_code` (`ru*` → ru, `en*` → en, anything else → uz), updated on `/start`; SMS uses the link's language if any, else uz. Telegram texts are plain text (no parse mode → no escaping of clinic names); buttons only for public https URLs (Telegram rejects localhost/http buttons with 400, which would fail the message). |
| BE4-12 | **Retention** (nightly 03:30 Asia/Tashkent and once at Worker start; `Retention:*`): OTP challenges > 24 h; sessions ended (revoked / expired / idle-dead) > 30 days; Sent/Skipped/Cancelled outbox rows > 30 days and **Failed > 90 days** (kept longer for investigation) with their attempts; link tokens > 24 h past expiry; dev capture > 7 days. Pending/Processing rows, audit and ledger are never touched. |
| BE4-13 | **Admin:** the list never shows payloads or texts; retry = Failed → Pending with attempts reset to 0, due now, audited `admin.notification.retried`; other statuses → 409 `admin.invalid_status_transition` (existing code reused). |
| BE4-14 | Templates live in code (`NotificationTemplates`, uz/ru/en), not in the database; `appointment.completed.patient` is a Telegram-only thank-you with the review hint. Still no `payment.*` intents (BE3-12). |

## 6. Mustaqil tekshiruv natijalari (2026-10-19)

Birlashgan tekshiruv (xavfsizlik, bir vaqtdagi so'rovlar, moliya, release) 10 ta topilma berdi. Har birining holati:

| Id | Daraja | Topilma | Qaror / holat |
|---|---|---|---|
| R1 | High | Tashqi TLS proksi ortida barcha mijozlar bitta IP bo'lib ko'rinardi, IP bo'yicha limitlar butun platforma uchun umumiy bo'lib qolardi | ✅ Tuzatildi: nginx `set_real_ip_from` (127.0.0.1 + backend tarmog'i, `OQTISH_TRUSTED_PROXY_CIDR`), `real_ip_recursive on`, API'ga faqat bitta tekshirilgan `X-Forwarded-For` qiymati uzatiladi, `X-Forwarded-Proto` ishonchli proksidan olinadi. Qo'lda tekshirildi: proksi bergan IP qabul qilinadi, soxta chap qism e'tiborga olinmaydi |
| R2 | Medium | Telegram/OTP orqali kirishda TOTP kodini cheksiz taxmin qilish mumkin edi | ✅ Tuzatildi: `TotpService` foydalanuvchi bo'yicha xato urinishlarni atomar hisoblaydi (barcha kirish yo'llari uchun), 5 ta xatodan keyin 15 daqiqaga bloklaydi, javob 429 `auth.totp_locked`. Test: `Wrong_totp_codes_lock_the_second_factor`. Migratsiya `ReviewHardening` |
| R3 | Medium | Sessiyaning mutlaq muddati yo'q edi (faol foydalanilsa abadiy yashardi) | ✅ Tuzatildi: `Auth:Session:AbsoluteLifetimeDays` (default 60 kun); sliding uzaytirish shu chegaradan oshmaydi |
| R4 | Low | Shifokor "o'zimniki" tekshiruvi qulfdan oldin bajarilardi | ✅ Tuzatildi: qulf olingach qayta tekshiriladi |
| R5 | Low | Yangi telefon/Telegram ulashda qo'shimcha tasdiq va eski kanalga xabar yo'q | ⏸ Ma'lum xavf sifatida qoldirildi: hisoblar hech qachon birlashtirilmaydi, ulash audit qilinadi. Keyingi versiyada: "yaqinda kirgan bo'lish" yoki TOTP talabi + eski kanalga xabar |
| R6 | Low | Parol xatolari hisoblagichi parallel so'rovlarda yo'qolishi va bloklash cheksiz uzayishi mumkin edi | ✅ Tuzatildi: atomar UPDATE; bloklangan paytdagi urinishlar hisoblanmaydi va blokni uzaytirmaydi |
| R7 | Low | Bir buxgalteriya qatori ikki hisob-kitobga tushishi | ✅ Muammo emas: `ix_settlement_items_ledger_line_id` allaqachon UNIQUE (migratsiyada tasdiqlandi) |
| R8 | Low | Komissiya qoidasini o'tgan sanadan boshlash mumkin | ✅ Ataylab ruxsat etilgan: mavjud shartnomani tizimga kiritishda kerak (oxirgi oy hisob-kitobi testi buni talab qiladi). Yaratish audit qilinadi, o'tgan yozuvlar qayta hisoblanmaydi |
| R9 | Low | Data Protection kalitlari bazada shifrlanmagan | 📋 Serverga chiqarish qadami (DEPLOYMENT.md): sertifikat/KMS bilan `ProtectKeysWith…` va zaxira nusxalarni shifrlash |
| R10 | Info | Production'da TOTP talabi o'chirilsa, faqat ogohlantirish chiqardi | ✅ Tuzatildi: Production'da ishga tushish rad etiladi |

Tekshirilgan va muammo topilmagan qismlar: klinikalar va filiallar orasidagi ajratish (IDOR), qabul tizimidagi qulflar tartibi va GiST cheklovi, buxgalteriya daftarining o'zgarmasligi va idempotentligi, Production uchun himoya tekshiruvlari, repoda sirlar yo'qligi.
