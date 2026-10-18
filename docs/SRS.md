# Software Requirements Specification — OqTish

Version 1.0 (2026-10-18). Sources: the project brief (master prompt), the business owner's answers,
and the decisions delegated on 2026-10-18 (`DECISIONS.md` §4). Each requirement has an id that tests
and documents refer to.

## 1. Purpose and scope

OqTish is a multi-tenant web platform for booking dental appointments in Uzbekistan. It connects
**patients** with independent **dental clinics** and their **branches**. Clinics run their operations
in a **clinic console**, and the **platform administrator** verifies clinics and settles commission.
A **Telegram bot and Mini App** offer booking and notifications inside Telegram.

Out of scope for v1:
- electronic medical records, diagnoses, dental images, treatment histories;
- online card payments collected by the platform;
- VAT invoicing;
- chairs/rooms as resources.

## 2. Actors

| Actor | Description |
|---|---|
| Guest | Unauthenticated visitor; browses approved clinics |
| Patient | Signed in by phone OTP or Telegram; books and manages own appointments |
| ClinicOwner, ClinicAdmin, Receptionist, Doctor, FinanceManager | Clinic staff, scoped to a clinic or to assigned branches (SECURITY.md §8) |
| PlatformAdmin | Operator with explicit, audited platform permissions |
| System | Worker processes: expiry, notifications, reminders, summaries, retention |

## 3. Functional requirements

### 3.1 Discovery (Guest, Patient)
- **FR-D1** Search approved, active clinics by text, service/category, district, distance (browser
  location on request), price, rating, and "free slot within 7 days".
- **FR-D2** Clinic profile shows branches (address, phone, opening hours), services with price
  (`Fixed` / `From`) and duration, rating, and published reviews.
- **FR-D3** Unapproved or suspended clinics are never visible publicly.

### 3.2 Accounts and authentication
- **FR-A1** Patients sign in with a phone OTP (SMS, +998 numbers) or Telegram (Mini App `initData` / Login Widget).
- **FR-A2** Staff and admins sign in with email + password. TOTP is mandatory for ClinicOwner, FinanceManager and platform admins.
- **FR-A3** Additional identities (phone, Telegram) are linked only after proof of control. Accounts are never merged by matching strings.
- **FR-A4** Users can list and revoke sessions and log out everywhere. Suspension revokes access immediately.
- **FR-A5** Staff password recovery uses a one-time code to the verified phone. Responses never reveal whether an account exists.
- **FR-A6** Patients can delete their account. Personal fields are anonymised, and financial records are kept de-identified.

### 3.3 Booking (Patient)
- **FR-B1** A patient chooses clinic, branch, service, date and a free start time, plus contact name and phone. **No doctor choice.**
- **FR-B2** The server computes availability from doctor working hours, time off, existing holds and service duration, with a 15-min grid, ≥ 60 min lead time and ≤ 30 days ahead.
- **FR-B3** A request is created as **Pending** with a provisional (hidden) doctor hold, so capacity can't be oversold. Concurrent requests for the same capacity: exactly one succeeds, the others get a conflict.
- **FR-B4** A Pending request **expires 30 minutes** after creation unless the clinic answers. The server is authoritative, and expired requests release their hold.
- **FR-B5** Max 3 Pending requests per patient.
- **FR-B6** Patients see their appointments, status history and the countdown. They can cancel Pending at any time and Confirmed until 2 h before start.
- **FR-B7** Patients can review a Completed visit (once, within 30 days). Reviews are moderated.
- **FR-B8** Patients can report a clinic or a review.

### 3.4 Clinic operations (staff, by permission)
- **FR-C1** Register a clinic (Pending verification). Manage the profile, branches and opening hours.
- **FR-C2** Manage services and branch prices/durations.
- **FR-C3** Invite staff by phone and assign roles and branch scope. No privilege escalation, and the last owner cannot be removed.
- **FR-C4** Manage doctors, their branches/services and weekly hours. Time off is managed by staff, and doctors manage their own.
- **FR-C5** Triage Pending requests: confirm **with an eligible doctor** (provides the service, works at the branch, free at that time) or reject. Confirming re-checks the deadline with server time. Confirm vs. expiry vs. cancel races resolve to exactly one outcome.
- **FR-C6** Cancel and reschedule Confirmed appointments. Mark Completed (with charged amount) or NoShow.
- **FR-C7** Walk-in/phone bookings are created directly as Confirmed.
- **FR-C8** Day calendar per branch (doctors × time).
- **FR-C9** Record payments (cash/card/transfer, partial or full), refunds (≤ net paid) and failed payments. View balances.
- **FR-C10** Branch-scoped daily and monthly financial reports, reproducible from the ledger. Settlements are viewable.
- **FR-C11** Clinic audit log (owner).

### 3.5 Platform administration
- **FR-P1** Verify, reject, suspend and reinstate clinics. Suspend and reinstate users.
- **FR-P2** Handle abuse reports and moderate reviews.
- **FR-P3** Configure commission contracts per clinic (basis, rate/fee, effective dates, contract reference). Rules are never edited, only ended.
- **FR-P4** Create, finalize and mark-paid monthly settlements (idempotent, concurrency-safe).
- **FR-P5** Break-glass read of tenant patient data, with a reason, audited.
- **FR-P6** View the platform audit log and notification delivery status. Retry failed notifications.

### 3.6 Notifications and Telegram
- **FR-N1** Telegram messages to linked users:
  - patient: request received, confirmed, rejected, cancelled by clinic, rescheduled, expired, reminders 24 h and 2 h before;
  - staff: new request, patient cancellation.
- **FR-N2** SMS fallback for confirmed, rejected, clinic-cancelled and rescheduled when Telegram is not linked or not deliverable.
- **FR-N3** Delivery uses a transactional outbox: bounded retries with backoff, recorded status, idempotent. A delivery failure never rolls back business data.
- **FR-N4** The bot links accounts via one-time deep links and opens the Mini App.
- **FR-N5** The Mini App offers patient booking and "my appointments", and staff triage of pending requests.
- **FR-N6** Messages contain no medical data, and staff messages never contain the patient's phone.

## 4. Financial requirements (details in FINANCIAL_RULES.md)

- **FR-F1** Appointment status, service completion, payment, ledger and settlement are separate records.
- **FR-F2** Money is decimal (`numeric(19,2)`), rounded per ledger line (AwayFromZero, 2 dp).
- **FR-F3** The double-entry, append-only ledger is idempotent per source event. Corrections are made only by reversal entries.
- **FR-F4** Commission only from an approved per-clinic rule. Rule id, rate and basis amount are snapshotted on each line. Without a rule, commission is zero and the report says so.
- **FR-F5** The reporting day is the branch-local date. Refunds post in their own period. Finalized months are locked.
- **FR-F6** Daily summaries are a rebuildable cache. Reconciliation flags any mismatch.

## 5. Non-functional requirements

| Id | Requirement |
|---|---|
| NFR-S1 | Tenant isolation enforced server-side by resource-level checks and DB composite foreign keys. Cross-tenant → 404, in-tenant missing permission → 403 |
| NFR-S2 | No secrets in the repository. Passwords hashed, OTP/tokens hashed, TOTP secrets encrypted |
| NFR-S3 | No phone numbers, names, codes, tokens or `initData` in logs or error responses |
| NFR-S4 | Rate limits on OTP, login and booking. CSRF defence via a required client header + SameSite cookies |
| NFR-S5 | HTTPS in deployed environments. Security headers (CSP, frame, referrer, permissions) |
| NFR-S6 | Least-privilege database roles. Ledger/audit tables are append-only at DB level |
| NFR-P1 | Personal data stored in Uzbekistan (hosting decision, legal sign-off before launch) |
| NFR-R1 | Backups daily, restore drill monthly (BACKUP_AND_RESTORE.md) |
| NFR-O1 | Structured JSON logs, `/health/live` and `/health/ready` |
| NFR-U1 | UI in Uzbek (Latin), Russian and English. Responsive at 360–1280 px. Accessible forms and states (loading, empty, validation, 403, 404, 409, 5xx) |
| NFR-T1 | Times stored in UTC and displayed in the branch time zone |
| NFR-Q1 | Builds have zero warnings. Unit + real-PostgreSQL integration + concurrency + frontend tests run in CI |

## 6. Acceptance criteria

The acceptance criteria are those of the brief (§17). They map to the test catalogue in `TESTING.md`:
- isolation: S-series;
- concurrency: B-series;
- finance: F/L-series;
- notifications: N-series;
- release: R-series.
