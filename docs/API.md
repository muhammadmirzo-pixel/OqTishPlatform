# API Reference and Contract

Base URL: `/api` (behind nginx on the same origin as the web app). This document is the contract
between backend and frontend and must match the code. OpenAPI is generated at `/openapi/v1.json`
(Development; CI exports it). Conventions (money, dates, pagination, errors, CSRF header,
idempotency) are in `docs/CONVENTIONS.md` §5. Every non-GET request needs `X-OqTish-Client`.

Auth column: **anon** = no sign-in; **user** = any signed-in user; **patient** = own data only;
**staff:perm** = clinic staff with that permission in the resource's clinic/branch;
**platform:perm** = platform admin grant.

## 0. Shared shapes

```ts
type Money = { amount: string; currency: 'UZS' }            // "1500000.00"
type Page<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
type Problem = { type: string; title: string; status: number; code: string; traceId: string; errors?: Record<string, string[]> }

type AppointmentStatus = 'Pending'|'Confirmed'|'Rejected'|'CancelledByPatient'|'CancelledByClinic'|'Expired'|'Completed'|'NoShow'
type StaffRole = 'ClinicOwner'|'ClinicAdmin'|'Receptionist'|'Doctor'|'FinanceManager'
type Weekday = 1|2|3|4|5|6|7                                   // ISO: 1 = Monday

type Me = {
  id: string; displayName: string
  phoneVerified: boolean; phoneMasked: string | null; email: string | null; telegramLinked: boolean; totpEnabled: boolean
  totpSetupRequired: boolean                      // some membership/platform grant waits for TOTP (BE1a)
  patient: { fullName: string; contactPhone: string | null } | null
  memberships: { membershipId: string; clinicId: string; clinicName: string; clinicStatus: ClinicVerification;
                 role: StaffRole; scope: 'Clinic'|'Branches'; branchIds: string[]; permissions: string[];
                 totpSetupRequired: boolean }[]   // permissions = effective ([] while TOTP setup is required)
  platformPermissions: string[]                  // effective ([] while TOTP setup is required)
}
type ClinicVerification = 'Pending'|'Approved'|'Suspended'|'Rejected'
type Session = { id: string; createdAt: string; lastSeenAt: string; expiresAt: string;
                 authMethod: 'Otp'|'Password'|'TelegramWebApp'|'TelegramWidget'; userAgent: string|null; current: boolean }

type AppointmentDto = {
  id: string; clinicId: string; clinicName: string; branchId: string; branchName: string; timeZone: string
  serviceId: string; serviceName: string; startsAt: string; endsAt: string; status: AppointmentStatus
  expiresAt: string | null                       // Pending only; server deadline
  quotedPrice: Money; chargedAmount: Money | null
  doctor: { id: string; name: string } | null     // null for patients while Pending
  contactName: string; contactPhone: string       // full phone only for staff with appointment.view; patients see their own
  source: 'Patient'|'WalkIn'
  canCancel: boolean                              // server-computed for the caller
  createdAt: string
  history?: { from: AppointmentStatus|null; to: AppointmentStatus; at: string; actor: 'Patient'|'Staff'|'System'; reasonCode: string|null }[]
}
```

## 1. Auth and session — `/api/auth`, `/api/me`

| Method & path | Auth | Purpose |
|---|---|---|
| `POST /auth/otp/request` `{ phone }` | anon | Sends a 6-digit code by SMS. → 202 `{ challengeId, expiresInSeconds, resendAfterSeconds }`. Rate-limited per phone and IP. Unknown and known numbers answer identically |
| `POST /auth/otp/verify` `{ challengeId, code }` | anon | Verifies the code and signs in, creating a patient user for a new phone. → 200 `Me` + session cookie. 5 attempts per challenge |
| `POST /auth/password/login` `{ email, password, totpCode? }` | anon | Staff/admin password sign-in. → 200 `Me`, 401 `auth.invalid_credentials`, 401 `auth.totp_required` |
| `POST /auth/password/reset/request` `{ email }` | anon | Sends a reset code to the account's verified phone. Always 202 `{ challengeId? }` |
| `POST /auth/password/reset/confirm` `{ challengeId, code, newPassword }` | anon | Sets a new password and revokes all sessions. → 204 |
| `POST /auth/telegram/webapp` `{ initData }` | anon | Mini App sign-in. Validates Telegram `initData` (HMAC, ≤ 24 h old). Signs in the user linked to that Telegram id, or creates a patient user. → 200 `Me` |
| `POST /auth/telegram/widget` `{ id, first_name, …, auth_date, hash }` | anon | Telegram Login Widget sign-in (website). → 200 `Me` |
| `POST /auth/logout` | user | Revokes the current session. → 204 |
| `POST /auth/logout-all` | user | Revokes every session of the user. → 204 |
| `GET /me` | user | Current user, memberships with effective permissions, platform permissions |
| `PUT /me/profile` `{ fullName }` | user | Updates the patient display name |
| `POST /me/password` `{ currentPassword?, newPassword }` | user | Sets/changes the password (staff). Needs a verified phone or an existing password |
| `POST /me/phone/request` `{ phone }` → `{ challengeId }`, `POST /me/phone/verify` `{ challengeId, code }` | user | Proves control of a phone and links it to the account. A phone already owned by another account → 409 `auth.identity_in_use` (never merged) |
| `POST /me/totp/setup` → `{ secret, otpauthUri }`, `POST /me/totp/enable` `{ code }`, `POST /me/totp/disable` `{ code }` | user | TOTP 2FA. Required for ClinicOwner, FinanceManager and platform admins before they can use those permissions |
| `POST /me/telegram/link` → `{ deepLink, expiresAt }` | user | One-time deep link `https://t.me/<bot>?start=<token>` (10 min). The bot completes the link |
| `DELETE /me/telegram` | user | Unlinks Telegram |
| `GET /me/sessions`, `DELETE /me/sessions/{id}` | user | Lists/revokes own sessions |
| `GET /me/invitations` | user | Pending staff invitations addressed to the caller's verified phone |
| `POST /me/invitations/{id}/accept` | user | Accepts an invitation (phone must match). Creates the membership |
| `POST /me/delete` | user | Anonymises the patient profile (OD-18). Staff must leave clinics first |

The session cookie `__Host-oqtish` is HttpOnly, Secure (except the http Development profile),
SameSite=Lax, and expires after 30 days sliding / 14 days idle. Sessions live server-side.

### 1.1 Implementation notes and deviations (BE1a, 2026-10-18)

Implemented: everything in the table above (`/me/invitations*` added in BE1b, see §4.1).

| Item | Contract as implemented |
|---|---|
| `POST /auth/otp/request` | 202 `{ challengeId, expiresInSeconds: 300, resendAfterSeconds: 60 }`. Same body for known/unknown numbers. Limits: 1 code per 60 s and 3 per 15 min per phone, 10 per 15 min per IP → 429 `auth.rate_limited` + `Retry-After`. Accepted phone forms: `+998 90 123-45-67`, `998901234567`, `00998…`, `(90) 123 45 67`, `901234567`; other → 400 `common.validation` (`errors.phone`) |
| `POST /auth/otp/verify` | **Deviation:** optional `totpCode`. Accounts with TOTP enabled get 401 `auth.totp_required` without it (ADR-017). Every submission counts as an attempt; the 5th wrong one and later answer `auth.otp_attempts_exceeded`. Suspended account → 403 `auth.account_suspended` |
| `POST /auth/password/login` | Locked account (10 consecutive failures, 15 min) also answers 401 `auth.invalid_credentials` (no lockout oracle) |
| `POST /auth/password/reset/request` | Always 202 `{ challengeId }` (a decoy id for unknown e-mails); 400 only for a malformed e-mail; limits as OTP, keyed by e-mail |
| `POST /auth/telegram/webapp` | **Deviation:** body `{ initData, totpCode? }`. 422 `telegram.disabled` when Telegram is not configured |
| `POST /auth/telegram/widget` | Body = the widget fields exactly as Telegram returns them (`id`, `first_name`, `last_name?`, `username?`, `photo_url?`, `auth_date`, `hash`) plus optional `totpCode` (excluded from the hash check) |
| `POST /auth/logout`, `/auth/logout-all` | 204 and the cookie is deleted |
| `GET /me` | `Me` above (additive fields `totpSetupRequired`, `memberships[].totpSetupRequired`) |
| `PUT /me/profile` | 200 `Me`; `fullName` 1–100 chars |
| `POST /me/password` | **Deviation:** body `{ email?, currentPassword?, newPassword }` (10–128 chars). First password: verified phone required (422 `auth.phone_verification_required`) and `email` required (login name; 409 `auth.identity_in_use` if taken). Change: `currentPassword` required (400 `auth.current_password_invalid`). 204; revokes the user's other sessions |
| `POST /me/phone/request` | 202, same body as `/auth/otp/request` (superset of `{ challengeId }`) |
| `POST /me/phone/verify` | 200 `Me`; 409 `auth.identity_in_use` (never merged, audited) |
| `POST /me/totp/setup` | 200 `{ secret (base32), otpauthUri }`; 409 `auth.totp_already_enabled` |
| `POST /me/totp/enable`, `/disable` `{ code }` | 204. Wrong code → **400** `auth.totp_invalid` (not 401, so the SPA does not sign out). No setup / not enabled → 409 `auth.totp_not_enabled`. Enable revokes other sessions |
| `POST /me/telegram/link` | 200 `{ deepLink, expiresAt }`; 422 `telegram.disabled` |
| `DELETE /me/telegram` | 204; 422 `auth.last_identity` when Telegram is the only sign-in method |
| `GET /me/sessions` | 200 `Session[]` (active, newest use first). `DELETE /me/sessions/{id}` → 204, other user's id → 404 |
| `POST /me/delete` | 204 (cookie deleted); 422 `account.delete_blocked` while a membership or platform grant exists |
| Errors | Every error is ProblemDetails with `code` + `traceId`; malformed JSON → 400 `common.validation`; invalid money → 400 `finance.amount_invalid`; framework 404/405/500 get `common.not_found` / `common.method_not_allowed` / `common.internal` |

## 2. Public catalogue — `/api/public` (anon)

| Method & path | Purpose |
|---|---|
| `GET /public/clinics?q&serviceCategory&serviceId&district&lat&lng&availableWithinDays&sort=rating\|price\|distance\|name&page&pageSize` | Search **approved, active** clinics. → `Page<ClinicCard>` |
| `GET /public/clinics/{clinicId}` | Clinic profile with active branches, hours and service offers. → `ClinicDetail`. Unapproved → 404 |
| `GET /public/clinics/{clinicId}/reviews?page` | Published reviews. → `Page<ReviewDto>` |
| `GET /public/branches/{branchId}/availability?serviceId&date=YYYY-MM-DD` | Free start times for that service and day (server-computed; doctors, hours, time off, holds). → `{ date, timeZone, durationMinutes, slots: { startsAt: string, localTime: string }[] }` |
| `GET /public/facets` | `{ districts: string[], categories: { key, name }[], services: { id, name, category }[] }` for filters |

```ts
type ClinicCard = { id: string; name: string; rating: number|null; reviewCount: number; minPrice: Money|null;
                    branchCount: number; districts: string[]; distanceKm: number|null; nextFreeSlot: string|null }
type ClinicDetail = { id: string; name: string; description: string; rating: number|null; reviewCount: number;
  branches: { id: string; name: string; district: string; address: string; latitude: number|null; longitude: number|null;
              timeZone: string; phone: string; hours: { weekday: Weekday; open: string; close: string }[];
              services: { serviceId: string; name: string; category: string; price: Money; priceKind: 'Fixed'|'From'; durationMinutes: number }[] }[] }
type ReviewDto = { id: string; rating: number; text: string; authorName: string; createdAt: string; branchName: string }
```

### 2.1 Implementation notes (BE1b, 2026-10-18)

Implemented: `GET /public/clinics`, `GET /public/clinics/{clinicId}`, `GET /public/facets`.
**Not yet:** `…/reviews` and `…/availability` (BE2).

| Item | Contract as implemented |
|---|---|
| Visibility | Only `Approved` clinics (Pending/Rejected/Suspended → invisible; detail → 404 `common.not_found`). Only active branches, and only active offers of active services. A clinic needs ≥ 1 matching active branch to be listed |
| `q` | ≤ 100 chars; case-insensitive "contains" on the clinic name **or** any offered service name |
| `serviceCategory` | A key from the fixed catalogue (`GET /public/facets` → `categories`); unknown → 400 |
| `serviceId`, `serviceCategory`, `district` | Clinic must have an active offer / branch that matches (`district` is case-insensitive exact). `minPrice` and `distanceKm` are computed over the **matching** branches/offers |
| `lat` + `lng` | Both or neither (400). `distanceKm` = distance to the nearest matching branch with coordinates (Haversine in SQL, R = 6371 km, 2 decimals) |
| `sort` | `rating` (default without coordinates; `rating` desc, nulls last, then `reviewCount` desc, name), `price` (`minPrice` asc, nulls last), `distance` (default with coordinates; requires `lat`/`lng`, else 400), `name`. Ties → name, id |
| `availableWithinDays` | Validated (1..30) and accepted; **the filter is applied after BE2** (availability engine). `nextFreeSlot` is `null` until then |
| `rating` / `reviewCount` | `null` / `0` until BE2 review moderation fills `tenancy.clinics.rating_average` / `review_count` |
| Paging | `page` ≥ 1, `pageSize` 1..100 (else 400) |
| `ClinicDetail` | As in the type above. `description` and branch `phone` are `""` when not set. Branches ordered by name; services by name |
| `GET /public/facets` | `{ districts: string[] (distinct, sorted, public branches only), categories: { key, name }[] (the whole fixed catalogue; `name` is the Uzbek default — localise by `key`), services: { id, name, category }[] (public offers, ≤ 500, by name) }` |

Service category keys: `diagnostics`, `hygiene`, `therapy`, `surgery`, `implants`, `prosthetics`, `orthodontics`, `pediatric`, `whitening`, `periodontics`, `other`.

## 3. Patient — `/api/me/appointments`, `/api/appointments` (patient)

| Method & path | Purpose |
|---|---|
| `POST /appointments` `{ branchId, serviceId, startsAt, contactName, contactPhone }` + `Idempotency-Key` | Creates a **Pending** request with an internal provisional doctor hold. → 201 `AppointmentDto` (no doctor). 409 `booking.slot_unavailable`; 422 `booking.outside_hours` / `booking.lead_time` / `booking.too_many_pending` / `booking.clinic_unavailable` |
| `GET /me/appointments?status&page` | Own appointments, newest first |
| `GET /me/appointments/{id}` | One own appointment with status history. Another patient's → 404 |
| `POST /me/appointments/{id}/cancel` | Pending: any time. Confirmed: until 2 h before start (`booking.cancel_too_late`) |
| `POST /me/appointments/{id}/review` `{ rating 1-5, text ≤ 1000 }` | Only Completed, own, within 30 days, once (`review.not_eligible`, `review.already_exists`) |
| `POST /reports` `{ targetType: 'Clinic'\|'Review', targetId, reason ≤ 500 }` | Abuse report to platform admins |

## 4. Clinic console — `/api/clinics/{clinicId}/…`

`clinicId` in the path is a lookup key only. Every handler verifies membership and branch scope
(404 cross-tenant, 403 in-tenant).

| Method & path | Permission | Purpose |
|---|---|---|
| `POST /clinics` `{ name, legalName, description, phone }` | user | Registers a new clinic (Pending verification). The caller becomes ClinicOwner |
| `GET /clinics/{clinicId}` | any membership | Clinic profile and verification status |
| `PUT /clinics/{clinicId}` | `clinic.profile.manage` | Edits the profile |
| `GET /clinics/{clinicId}/dashboard?branchId` | any membership | Today's counts (pending, confirmed, completed, no-show). The collected sum is included only with `finance.report.view` |
| `GET /clinics/{clinicId}/branches` · `POST …/branches` · `PUT …/branches/{branchId}` | read: membership · write: `branch.manage` | Branches (name, district, address, lat/lng, phone, timeZone, isActive) |
| `PUT …/branches/{branchId}/hours` `[{ weekday, open, close }]` | `branch.manage` | Opening hours |
| `GET …/services` · `POST …/services` · `PUT …/services/{serviceId}` | read: membership · write: `catalog.manage` | Clinic services (name, category, isActive) |
| `PUT …/branches/{branchId}/services/{serviceId}` `{ price, priceKind, durationMinutes, isActive }` | `catalog.manage` | Branch offer (price/duration) |
| `GET …/staff` · `PUT …/staff/{membershipId}` `{ role, scope, branchIds }` · `DELETE …/staff/{membershipId}` | `staff.manage` | Memberships. Only owners grant ClinicOwner/ClinicAdmin. No self-escalation |
| `GET …/invitations` · `POST …/invitations` `{ phone, role, scope, branchIds }` · `DELETE …/invitations/{id}` | `staff.manage` | Staff invitations by phone (7-day expiry) |
| `GET …/doctors` · `POST …/doctors` · `PUT …/doctors/{doctorId}` `{ displayName, isActive, branchIds, serviceIds, userId? }` | read: membership · write: `schedule.manage` | Doctors and their branch/service links |
| `GET …/doctors/{doctorId}/working-hours` · `PUT …` `[{ branchId, weekday, start, end }]` | read: membership · write: `schedule.manage` | Weekly templates (branch-local times) |
| `GET …/doctors/{doctorId}/time-off?from&to` · `POST …` `{ startsAt, endsAt, reason? }` · `DELETE …/time-off/{id}` | `schedule.manage`, or the doctor for own | Time off. Overlap with active appointments → 409 `schedule.timeoff_conflict` |
| `GET …/appointments?branchId&status&from&to&doctorId&page` | `appointment.view` (Doctor: own) | Appointment list (in scope only) |
| `GET …/appointments/{id}` | `appointment.view` | Detail with history |
| `GET …/appointments/{id}/eligible-doctors` | `appointment.triage` | Doctors who provide the service at that branch and are free for that time |
| `POST …/appointments/{id}/confirm` `{ doctorId }` | `appointment.triage` | Pending → Confirmed. Rechecks the deadline (DB time), eligibility and overlap atomically. 409 `booking.expired` / `booking.not_pending` / `booking.doctor_busy`; 422 `booking.doctor_not_eligible` |
| `POST …/appointments/{id}/reject` `{ reasonCode }` | `appointment.triage` | Pending → Rejected. Releases the hold |
| `POST …/appointments/{id}/cancel` `{ reasonCode }` | `appointment.cancel` | Confirmed → CancelledByClinic. Releases the hold |
| `POST …/appointments/{id}/reschedule` `{ startsAt, doctorId }` | `appointment.triage` | Moves a Confirmed appointment atomically |
| `POST …/appointments/{id}/complete` `{ chargedAmount }` | `appointment.outcome` (Doctor: own) | Confirmed → Completed (from start time). Creates the ServiceCompletion and the revenue ledger entry |
| `POST …/appointments/{id}/no-show` | `appointment.outcome` (Doctor: own) | Confirmed → NoShow (from start + 15 min) |
| `POST …/appointments` `{ branchId, serviceId, startsAt, doctorId, contactName, contactPhone }` + `Idempotency-Key` | `appointment.create_walkin` | Walk-in/phone booking. Created directly as Confirmed |
| `GET …/calendar?branchId&date` | `appointment.view` | Day view: doctors × blocks (appointments, holds, time off) in branch time |
| `GET …/payments?branchId&from&to&page` | `payment.record` or `finance.report.view` | Payments list |
| `GET …/appointments/{id}/balance` | `payment.record` or `finance.report.view` | `{ charged, paid, refunded, outstanding }` |
| `POST …/appointments/{id}/payments` `{ amount, method: 'Cash'\|'Card'\|'Transfer', externalRef? }` + `Idempotency-Key` | `payment.record` | Records a payment (any status ≥ Confirmed). Posts ledger + commission entries |
| `POST …/payments/{paymentId}/fail` `{ reason }` | `payment.record` | Marks a recorded-but-bounced card payment as Failed, via reversal entries |
| `POST …/payments/{paymentId}/refund` `{ amount, reason }` + `Idempotency-Key` | `payment.refund` | Partial/full refund ≤ net paid (`finance.refund_exceeds_paid`) |
| `GET …/reports/daily?branchId&from&to` | `finance.report.view` | Per day (branch-local): visits, charged, collected, refunded, commission. Rebuilt from the ledger |
| `GET …/reports/monthly?year&month&branchId` | `finance.report.view` | Monthly totals plus the reconciliation flag |
| `GET …/settlements` | `finance.settlement.view` | Clinic's settlements |
| `GET …/audit?page` | `audit.view` (owner) | Clinic audit log |

### 4.1 Implementation notes and DTOs (BE1b, 2026-10-18)

Implemented in BE1b: clinic registration/profile, dashboard (counts are 0 until BE2/BE3 register
contributors), branches + hours, services + branch offers, staff + invitations (+ `/me/invitations`),
doctors + links + working hours, clinic audit. Time off, appointments, calendar, payments, reports,
settlements: later stages.

Authorization pattern everywhere: the resource is loaded by id **within the route clinic**; another
clinic's id → 404 `common.not_found`; no membership → 404; in-tenant without permission or outside a
Branches-scoped membership → 403 `common.forbidden` (TOTP rule → 403 `auth.totp_setup_required`).

```ts
type ClinicProfileDto = { id: string; name: string; legalName: string; description: string; phone: string
                          verificationStatus: ClinicVerification; statusReason: string | null; createdAt: string }
type DashboardDto = { date: string /* YYYY-MM-DD, branch/clinic local */; branchId: string | null
                      pending: number; confirmed: number; completed: number; noShow: number; collected: Money | null }
type BranchDto = { id; name; district; address; latitude: number|null; longitude: number|null; phone: string /* "" if none */
                   timeZone: string; isActive: boolean; hours: { weekday: Weekday; open: 'HH:mm'; close: 'HH:mm' }[] }
type ServiceDto = { id; name; category: string /* category key */; isActive: boolean
                    offers: { branchId; price: Money; priceKind: 'Fixed'|'From'; durationMinutes: number; isActive: boolean }[] }
type StaffMemberDto = { membershipId; userId; displayName; phoneMasked: string|null; role: StaffRole; scope: 'Clinic'|'Branches'
                        branchIds: string[]; status: 'Active'|'Suspended' }
type InvitationDto = { id; phoneMasked; role: StaffRole; scope; branchIds: string[]; createdAt; expiresAt }
type MyInvitationDto = { id; clinicId; clinicName; role: StaffRole; scope; branchNames: string[]; expiresAt }
type DoctorDto = { id; displayName; isActive: boolean; branchIds: string[]; serviceIds: string[]; userId: string|null }
type WorkingHoursEntry = { branchId; weekday: Weekday; start: 'HH:mm'; end: 'HH:mm' }
type AuditEntryDto = { id; occurredAt; actorKind: 'User'|'System'|'Anonymous'; actorUserId: string|null; actorName: string|null
                       action: string; clinicId: string|null; branchId: string|null; targetType: string|null; targetId: string|null }
```

| Endpoint | Request → response; rules |
|---|---|
| `POST /clinics` | `{ name (1–200), legalName? (≤300), description? (≤4000), phone? }` → **201** `ClinicProfileDto` (Pending). Phones are normalised to E.164 (+998…). At most 3 Pending registrations per user → 422 `clinic.registration_limit` |
| `GET /clinics/{id}` | any membership → `ClinicProfileDto` |
| `PUT /clinics/{id}` | same body as POST → 200 `ClinicProfileDto`; `clinic.profile.manage` **clinic-wide** (Branches-scoped admin → 403) |
| `GET …/dashboard?branchId` | any membership → `DashboardDto`. Without `branchId`: the caller's accessible branches. `branchId` outside scope → 403, another clinic's → 404. `collected` is `Money` (zero until BE3) with `finance.report.view`, else `null` |
| `GET …/branches` | any membership → `BranchDto[]` (Branches-scoped: own branches only), by name |
| `POST …/branches` | `{ name, district, address (required), latitude?, longitude? (both or neither, ≤ 6 decimals), phone?, timeZone? ('Asia/Tashkent' default or 'Asia/Samarkand'), isActive? }` → **201** `BranchDto`; `branch.manage` clinic-wide |
| `PUT …/branches/{branchId}` | same body → 200 `BranchDto`; `branch.manage` on that branch |
| `PUT …/branches/{branchId}/hours` | `[{ weekday 1–7, open 'HH:mm', close 'HH:mm' }]` (≤ 28, open < close, no overlap per weekday) → **204**; replaces all hours |
| `GET …/services` | any membership → `ServiceDto[]`; `offers` only for branches in the caller's scope |
| `POST …/services`, `PUT …/services/{serviceId}` | `{ name (1–200), category (key), isActive? }` → 201 / 200 `ServiceDto`; `catalog.manage` **clinic-wide** (services belong to the whole clinic) |
| `PUT …/branches/{branchId}/services/{serviceId}` | `{ price: Money ≥ 0, priceKind, durationMinutes 5–480, isActive? }` → **204** (upsert); `catalog.manage` on the branch. More than 2 decimals → 400 `finance.amount_invalid` |
| `GET …/staff` | `staff.manage` → `StaffMemberDto[]` (non-removed). Branches-scoped managers see only Branches-scoped memberships inside their branches |
| `PUT …/staff/{membershipId}` | `{ role, scope, branchIds }` → 200 `StaffMemberDto`. 403 `staff.cannot_grant_role` when: the actor's role may not manage the target's current **or** new role (owners: all; ClinicAdmin: Receptionist/Doctor/FinanceManager), the scope/branches exceed the actor's own, or the target is the caller. 422 `staff.last_owner` |
| `DELETE …/staff/{membershipId}` | → 204; status Removed, effective on the next request. Leaving (own membership) is allowed unless last owner (422 `staff.last_owner`) |
| `GET …/invitations` | `staff.manage` → open (not accepted/revoked) `InvitationDto[]`, newest first; expired ones are included (compare `expiresAt`) |
| `POST …/invitations` | `{ phone, role, scope, branchIds }` → **201** `InvitationDto`; same escalation rules; 7-day expiry; an open invitation for the same phone is revoked (replaced); phone already a member → 409 `staff.already_member`. **No SMS is sent** in v1 — the invitee sees it in `GET /me/invitations` |
| `DELETE …/invitations/{id}` | → 204 (revoked); same escalation rules |
| `GET /me/invitations` | user → `MyInvitationDto[]` — open, unexpired invitations for the caller's **verified** phone ( `[]` without one); suspended clinics excluded |
| `POST /me/invitations/{id}/accept` | → **204**. Not addressed to the caller's verified phone / revoked / accepted → 404; expired (DB time) → 422 `staff.invitation_expired`; already an active member → 409 `staff.already_member`. A removed membership is reactivated with the invited grant |
| `GET …/doctors` | any membership → `DoctorDto[]` (Branches-scoped: doctors linked to one of their branches) |
| `POST …/doctors`, `PUT …/doctors/{doctorId}` | `{ displayName (1–200), isActive?, branchIds?, serviceIds?, userId? }` → 201 / 200 `DoctorDto`; links are **replaced**. `schedule.manage` on every branch before and after the change. Changing `userId` also needs `staff.manage`; the user must have a non-removed membership in the clinic (422 `schedule.doctor_user_invalid`) and must not be another doctor's user (409 `schedule.doctor_user_taken`). Unknown/other-clinic branch or service ids → 400. Removing a branch deletes the doctor's working hours there |
| `GET …/doctors/{doctorId}/working-hours` | any membership → `WorkingHoursEntry[]` (entries of branches outside a Branches-scoped member's scope omitted) |
| `PUT …/doctors/{doctorId}/working-hours` | `WorkingHoursEntry[]` (≤ 100) → **204**. Replaces the entries of the branches where the caller has `schedule.manage`; other branches' entries are kept (sending one → 403). Branch not linked → 422 `schedule.branch_not_linked`; overlap on a weekday across all branches → 422 `schedule.hours_overlap` |
| `GET …/audit?page&pageSize` | `audit.view` (owner, clinic-wide) → `Page<AuditEntryDto>` newest first |

## 5. Platform admin — `/api/admin` (platform permissions, TOTP required)

| Method & path | Permission | Purpose |
|---|---|---|
| `GET /admin/dashboard` | any platform permission | Counts: clinics by status, appointments today, open reports |
| `GET /admin/clinics?status&q&page` | `platform.clinic.verify` | Clinic list for review |
| `POST /admin/clinics/{id}/approve` · `/reject {reason}` · `/suspend {reason}` · `/reinstate` | `platform.clinic.verify` / `platform.account.suspend` | Verification and suspension. Audited |
| `GET /admin/users?q&page` · `POST /admin/users/{id}/suspend {reason}` · `/reinstate` | `platform.account.suspend` | Accounts. Suspension revokes sessions |
| `GET /admin/reports?status&page` · `POST /admin/reports/{id}/resolve {resolution}` | `platform.reports.handle` | Abuse reports |
| `GET /admin/reviews?status&page` · `POST /admin/reviews/{id}/publish` · `/hide {reason}` | `platform.reports.handle` | Review moderation |
| `GET /admin/clinics/{id}/commission-rules` · `POST …` `{ basis, rate?, fixedFee?, effectiveFrom, effectiveTo?, contractReference }` · `POST …/commission-rules/{ruleId}/end {effectiveTo}` | `platform.finance.settle` | Commission contracts (no overlapping ranges; rules are never edited, only ended) |
| `GET /admin/settlements?clinicId&status` · `POST /admin/settlements {clinicId, year, month}` · `POST /admin/settlements/{id}/finalize` · `/mark-paid {paidAt, reference}` · `/void {reason}` | `platform.finance.settle` | Monthly settlements (idempotent, concurrency-safe) |
| `GET /admin/clinics/{id}/appointments?…` + header `X-Access-Reason` | `platform.tenant-data.read` | Break-glass read of tenant patient data. Audited with the reason |
| `GET /admin/audit?clinicId&action&page` | `platform.audit.view` | Platform audit log |
| `GET /admin/notifications?status&page` · `POST /admin/notifications/{id}/retry` | `platform.audit.view` | Outbox delivery status / manual retry |

### 5.1 Implementation notes and DTOs (BE1b, 2026-10-18)

Implemented: dashboard, clinics (list/approve/reject/suspend/reinstate), users (list/suspend/reinstate),
audit. Reports, reviews, commission rules, settlements, break-glass reads and notifications come later.
Every platform check answers 403 `common.forbidden` without the grant and 403 `auth.totp_setup_required`
while TOTP is not enabled (`Security:RequireTotpForPrivilegedRoles`). Permission split (as the UI
assumed): approve/reject = `platform.clinic.verify`; suspend/reinstate = `platform.account.suspend`.
Note for BE2: the break-glass header `X-Access-Reason` arrives **percent-encoded (UTF-8)** from the UI and
must be decoded by the server.

```ts
type AdminDashboardDto = { clinicsByStatus: Record<ClinicVerification, number>; appointmentsToday: number /* 0 until BE2 */
                           openReports: number /* 0 until BE2 */; activeUsers: number; suspendedUsers: number }
type AdminClinicDto = { id; name; legalName: string; phone: string; verificationStatus: ClinicVerification; isSuspended: boolean
                        statusReason: string|null; branchCount: number; createdAt: string }
type AdminUserDto = { id; displayName; phoneMasked: string|null; email: string|null; status: 'Active'|'Suspended'; statusReason: string|null; createdAt }
```

| Endpoint | Rules |
|---|---|
| `GET /admin/dashboard` | any effective platform permission |
| `GET /admin/clinics?status&q&page&pageSize` | `Page<AdminClinicDto>`; Pending first, then newest; `q` = name / legal name contains |
| `POST /admin/clinics/{id}/approve` | Pending or Rejected → Approved → 200 `AdminClinicDto` |
| `POST …/reject { reason }` | Pending → Rejected (`reason` 1–1000, shown to the owner as `statusReason`) |
| `POST …/suspend { reason }` | Approved → Suspended: hidden from public, every membership behaves as "no membership" on the next request |
| `POST …/reinstate` | Suspended → Approved |
| | Other transitions → 409 `admin.invalid_status_transition`; unknown id → 404 |
| `GET /admin/users?q&page&pageSize` | `Page<AdminUserDto>` (deleted accounts excluded); `q` = display name / e-mail contains, or an exact phone |
| `POST /admin/users/{id}/suspend { reason }` | → 204; rotates the security stamp and revokes every session (next request → 401). Self → 422 `admin.cannot_suspend_self`; not Active → 409 |
| `POST /admin/users/{id}/reinstate` | Suspended → Active → 204 |
| `GET /admin/audit?clinicId&action&page&pageSize` | `Page<AuditEntryDto>`; `action` exact, or a prefix ending in `.` (e.g. `staff.`) |

### 5.2 Bootstrap CLI (not HTTP)

`dotnet OqTish.Api.dll create-platform-admin --email <e-mail> --phone <+998…>` — see docs/SERVICES.md §2.8.

## 6. Telegram

| Method & path | Auth | Purpose |
|---|---|---|
| `POST /telegram/webhook/{secretPath}` | Telegram (secret path + `X-Telegram-Bot-Api-Secret-Token`) | Bot updates when `Telegram:Mode=Webhook`. In `Polling` mode (default for local work) the Worker calls `getUpdates` instead |
| `GET /telegram/config` | anon | `{ enabled, botUsername, miniAppUrl }` for the UI. `enabled` is true only when `Telegram:Enabled` and a bot token + username are configured; the other two are null otherwise |

Bot behaviour: `/start <linkToken>` links the chat to the account that created the token. `/start`
alone sends a welcome message with a "Open OqTish" Mini App button. `/stop` unlinks.
`/appointments` lists upcoming appointments for linked patients.

## 7. Development-only — `/api/dev` (404 outside Development)

| Method & path | Purpose |
|---|---|
| `GET /dev/sms?phone` | Messages captured by the Fake SMS sender (to read OTP codes during local testing) → `[{ text, sentAt }]`, newest first |
| `GET /dev/telegram` | Messages the Fake Telegram client would have sent |
| `POST /dev/seed` | Idempotently loads fictional demo data (same as `dotnet run --project src/OqTish.Api -- seed`, which is allowed in Development and Staging and always refused in Production) → `{ contributors: string[] }`. Identity seed: admin `admin@oqtish.local` / `Seed:AdminPassword` (dev-only default `Admin123!demo`) with all platform permissions; demo patient `+998900000001`. BE1b adds 3 approved demo clinics in Tashkent + 1 Pending, services, doctors, hours and demo staff logins for every role (`Seed:DemoStaffAccounts`, Development only) — credentials in docs/SERVICES.md §2.9 |

## 8. Health

`GET /health/live` (process up), `GET /health/ready` (DB reachable + no pending migrations).

## 9. Error codes

| Code | HTTP | Meaning |
|---|---|---|
| `common.validation` | 400 | Field errors in `errors` |
| `common.not_found` | 404 | Resource doesn't exist or isn't visible to the caller |
| `common.forbidden` | 403 | Visible but not permitted |
| `common.idempotency_mismatch` | 422 | Key reused with a different body |
| `common.concurrency` | 409 | Retry exhausted, please retry |
| `security.client_header_missing` | 403 | CSRF header missing |
| `auth.unauthenticated` | 401 | Sign-in required |
| `auth.invalid_credentials` | 401 | Wrong email/password |
| `auth.totp_required` / `auth.totp_invalid` | 401 (400 on `/me/totp/*`) | Second factor |
| `auth.totp_setup_required` | 403 | Privileged role without TOTP |
| `auth.otp_invalid` / `auth.otp_expired` / `auth.otp_attempts_exceeded` | 400 | OTP problems |
| `auth.rate_limited` | 429 | Too many requests |
| `auth.identity_in_use` | 409 | Phone/Telegram already linked to another account |
| `auth.account_suspended` | 403 | Suspended user |
| `auth.telegram_invalid` | 401 | Bad or stale `initData` / widget hash |
| `auth.totp_already_enabled` / `auth.totp_not_enabled` | 409 | TOTP state conflict (BE1a) |
| `auth.current_password_invalid` | 400 | Wrong current password on `POST /me/password` (BE1a) |
| `auth.phone_verification_required` | 422 | First password needs a verified phone (BE1a) |
| `auth.last_identity` | 422 | Cannot unlink the only sign-in method (BE1a) |
| `account.delete_blocked` | 422 | Leave clinics / platform roles before deleting the account (BE1a) |
| `telegram.disabled` | 422 | Telegram integration not configured (BE1a) |
| `telegram.link_invalid` | 422 | Bot deep-link token invalid, used or expired (bot only, BE4) |
| `common.method_not_allowed` / `common.internal` | 405 / 500 | Framework-generated problems (BE1a) |
| `booking.slot_unavailable` | 409 | No eligible doctor free for that interval |
| `booking.outside_hours` | 422 | Not within working hours |
| `booking.lead_time` | 422 | Too soon / beyond horizon |
| `booking.too_many_pending` | 422 | Patient already has 3 Pending |
| `booking.clinic_unavailable` | 422 | Clinic/branch/service inactive or unapproved |
| `booking.not_pending` | 409 | Status changed meanwhile |
| `booking.expired` | 409 | Deadline passed |
| `booking.doctor_not_eligible` | 422 | Doctor doesn't provide the service at that branch |
| `booking.doctor_busy` | 409 | Doctor overlaps another block |
| `booking.invalid_transition` | 409 | State machine rejects the action |
| `booking.cancel_too_late` | 422 | Patient cutoff passed |
| `booking.too_early_for_outcome` | 422 | Complete/no-show before allowed time |
| `schedule.timeoff_conflict` | 409 | Time off overlaps appointments |
| `staff.cannot_grant_role` | 403 | Escalation attempt |
| `staff.last_owner` | 422 | Cannot remove the last owner |
| `staff.already_member` | 409 | The phone/user is already a member of the clinic (BE1b) |
| `staff.invitation_expired` | 422 | Invitation past its 7-day expiry (BE1b) |
| `clinic.registration_limit` | 422 | Too many Pending clinic registrations for this user (BE1b) |
| `schedule.hours_overlap` | 422 | Working-hour intervals overlap on a weekday (BE1b) |
| `schedule.branch_not_linked` | 422 | Working hours for a branch the doctor is not linked to (BE1b) |
| `schedule.doctor_user_invalid` | 422 | Doctor `userId` is not a member of the clinic (BE1b) |
| `schedule.doctor_user_taken` | 409 | The user is already linked to another doctor of the clinic (BE1b) |
| `admin.invalid_status_transition` | 409 | Clinic/account not in a state allowing the action (BE1b) |
| `admin.cannot_suspend_self` | 422 | A platform admin cannot suspend their own account (BE1b) |
| `finance.refund_exceeds_paid` | 422 | Refund too large |
| `finance.amount_invalid` | 400 | Amount ≤ 0 or > 2 decimals |
| `finance.period_locked` | 409 | Posting into a finalized month (redirected) |
| `finance.settlement_exists` | 409 | Settlement already exists for the period |
| `finance.rule_overlap` | 409 | Commission rule overlaps an existing one |
| `review.not_eligible` / `review.already_exists` | 422 / 409 | Review rules |
