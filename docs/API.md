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
  priceKind: 'Fixed'|'From'; hasReview: boolean; canReview: boolean   // additive (BE2)
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

Implemented: `GET /public/clinics`, `GET /public/clinics/{clinicId}`, `GET /public/facets`; BE2 added
`…/reviews`, `…/availability`, `availableWithinDays` and `nextFreeSlot` (§2.2).

| Item | Contract as implemented |
|---|---|
| Visibility | Only `Approved` clinics (Pending/Rejected/Suspended → invisible; detail → 404 `common.not_found`). Only active branches, and only active offers of active services. A clinic needs ≥ 1 matching active branch to be listed |
| `q` | ≤ 100 chars; case-insensitive "contains" on the clinic name **or** any offered service name |
| `serviceCategory` | A key from the fixed catalogue (`GET /public/facets` → `categories`); unknown → 400 |
| `serviceId`, `serviceCategory`, `district` | Clinic must have an active offer / branch that matches (`district` is case-insensitive exact). `minPrice` and `distanceKm` are computed over the **matching** branches/offers |
| `lat` + `lng` | Both or neither (400). `distanceKm` = distance to the nearest matching branch with coordinates (Haversine in SQL, R = 6371 km, 2 decimals) |
| `sort` | `rating` (default without coordinates; `rating` desc, nulls last, then `reviewCount` desc, name), `price` (`minPrice` asc, nulls last), `distance` (default with coordinates; requires `lat`/`lng`, else 400), `name`. Ties → name, id |
| `availableWithinDays` | 1..30 (else 400). **BE2:** only clinics with ≥ 1 free slot (§2.2 algorithm) for a matching offer at a matching branch on the branch-local days today … today + N − 1; applied before paging |
| `nextFreeSlot` | **BE2:** earliest free slot (UTC instant) of the clinic within `availableWithinDays` days (default 7), over the matching branches/offers; `null` when none |
| `rating` / `reviewCount` | Average (2 decimals) and count of **Published** reviews, maintained by review moderation (BE2); `null` / `0` without one |
| Paging | `page` ≥ 1, `pageSize` 1..100 (else 400) |
| `ClinicDetail` | As in the type above. `description` and branch `phone` are `""` when not set. Branches ordered by name; services by name |
| `GET /public/facets` | `{ districts: string[] (distinct, sorted, public branches only), categories: { key, name }[] (the whole fixed catalogue; `name` is the Uzbek default — localise by `key`), services: { id, name, category }[] (public offers, ≤ 500, by name) }` |

### 2.2 Implementation notes (BE2, 2026-10-18)

| Endpoint | Contract as implemented |
|---|---|
| `GET /public/branches/{branchId}/availability?serviceId&date` | anon. `serviceId` required, `date` = branch-local `YYYY-MM-DD` within today … today + 30 (else 400 `common.validation`). Branch inactive / clinic not Approved / offer or service inactive / unknown → 404. → `{ date, timeZone, durationMinutes, slots: [{ startsAt (UTC), localTime 'HH:mm' }] }` |
| `GET /public/clinics/{clinicId}/reviews?page&pageSize` | anon. Approved clinic (else 404). Published reviews, newest first → `Page<ReviewDto>`; `authorName` = first name + initial of the last name (`"Aziza K."`), `"Bemor"` when unknown/anonymised |

**Availability algorithm.** A start time S (branch-local grid of 15 minutes from local midnight, slots never
cross local midnight) is offered iff S ≥ now + 60 min (database time), [S, S + duration) lies inside a branch
opening interval of that weekday (only when the branch has opening hours configured at all), and at least one
eligible doctor — active, linked to the branch, providing the service — has a working-hours interval covering
[S, S + duration) in branch time and no active time block (hold, appointment, time off) overlapping it. Holds of
overdue Pending requests are ignored (they are expired on the next write). The list is advisory: booking
recomputes everything under the branch lock.

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

### 3.1 Implementation notes (BE2, 2026-10-18)

All endpoints need a signed-in user; another patient's appointment → 404 `common.not_found`.

| Endpoint | Contract as implemented |
|---|---|
| `POST /appointments` | Body as above (`contactPhone` any accepted Uzbek form, normalised to E.164; `contactName` 1–100). Optional `Idempotency-Key` (≤ 64 printable ASCII): replay → same 201 body; same key + different body → 422 `common.idempotency_mismatch`. → **201** `AppointmentDto` (Pending, `doctor: null`, `expiresAt` = database time + 30 min). Errors: 400 `common.validation` (fields; start not on the 15-minute grid); 404 unknown branch; 422 `booking.clinic_unavailable` (clinic not Approved, branch/offer/service inactive), `booking.lead_time` (< 60 min ahead or after today + 30 days, branch time), `booking.outside_hours` (no eligible doctor works then — working hours, time off, branch opening hours), `booking.too_many_pending` (3 Pending already); 409 `booking.slot_unavailable` (every working doctor busy); 409 `common.concurrency` (retries exhausted); 429 `auth.rate_limited` (`RateLimiting:BookingPermitPerHour`, default 10 per session) |
| `GET /me/appointments?status&page&pageSize` | `Page<AppointmentDto>` newest first (by creation). `status=Pending` excludes overdue requests, `status=Expired` includes them |
| `GET /me/appointments/{id}` | `AppointmentDto` with `history`. The doctor is shown only once the appointment is no longer Pending |
| `POST /me/appointments/{id}/cancel` | No body. Pending (not yet expired) any time; Confirmed until start − 2 h (database time) else 422 `booking.cancel_too_late`; overdue Pending → 409 `booking.expired`; other statuses → 409 `booking.invalid_transition`. → 200 `AppointmentDto` (CancelledByPatient, with history) |
| `POST /me/appointments/{id}/review` | `{ rating 1–5, text ≤ 1000 (may be empty) }` → **201** `{ id, appointmentId, rating, text, status: 'Pending', createdAt }`. Not Completed / more than 30 days after completion → 422 `review.not_eligible`; second review → 409 `review.already_exists`. Published only after moderation |
| `POST /reports` | `{ targetType: 'Clinic'\|'Review', targetId, reason 1–500 }` → **201** `{ id, status: 'Open' }`. Target must be public (Approved clinic / Published review) else 404. An open report of the same reporter for the same target is returned again (idempotent) |

Read rules for every `AppointmentDto`: an overdue Pending request is returned as `status: 'Expired'`
(`expiresAt` kept); `canCancel` is computed for the caller (patient: rule above; staff: Confirmed and
`appointment.cancel` on the branch — Pending requests are rejected, not cancelled); `canReview`: own,
Completed, ≤ 30 days, no review yet; `hasReview`: a review exists (FRONTEND.md §9 gap 10).

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
doctors + links + working hours, clinic audit. BE2: time off, appointments, calendar, walk-ins, dashboard
appointment counts (§4.2). BE3: payments, balance, refunds, reports, settlements, dashboard `collected` (§4.3).

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

### 4.2 Booking in the console (BE2, 2026-10-18)

Every handler loads the appointment **within the route clinic** (other clinic's id → 404), checks the
permission on the appointment's branch (no membership → 404; role lacks it or branch outside a Branches-scoped
membership → 403), and for the Doctor role (own-only) treats appointments not assigned to its doctor record as
404. Lists filter in SQL. FinanceManager has `appointment.view` only: every transition → 403. Time rules use
**database time**.

```ts
type EligibleDoctor = { id: string; name: string; withinWorkingHours: boolean; provisional: boolean } // additive fields
type CalendarDto = { date: string; timeZone: string; branchId: string
  doctors: { id: string; name: string; workingHours: { start: 'HH:mm'; end: 'HH:mm' }[]
             blocks: { kind: 'Appointment'|'Hold'|'TimeOff'; startsAt: string; endsAt: string; appointmentId: string|null
                       status: AppointmentStatus|null; serviceName: string|null; contactName: string|null
                       otherBranch: boolean /* busy at another branch, no details */; blockId: string|null /* time off id */ }[] }[] }
type TimeOffDto = { id: string; startsAt: string; endsAt: string; reason: string|null }
```

| Endpoint | Contract as implemented |
|---|---|
| `GET …/appointments?branchId&status&from&to&doctorId&page&pageSize` | `appointment.view` → `Page<AppointmentDto>` ordered by `startsAt`. `from`/`to` = UTC instants, **half-open [from, to) on `startsAt`** (FRONTEND.md F4); `from ≥ to` → 400. `branchId` outside scope → 403, other clinic's → 404. Doctor: only appointments assigned to it (Pending requests never) |
| `GET …/appointments/{id}` | `appointment.view` → `AppointmentDto` with `history` (staff see the full contact phone) |
| `GET …/appointments/{id}/eligible-doctors` | `appointment.triage` → `EligibleDoctor[]`: active, linked to the branch, providing the service, no active block overlapping the interval (the request's own hold excluded). Order: provisional holder, within working hours, name |
| `POST …/appointments/{id}/confirm` `{ doctorId }` | `appointment.triage`. Pending only (409 `booking.not_pending`), deadline by database time (409 `booking.expired`), doctor of another clinic / not linked / not providing / inactive → 422 `booking.doctor_not_eligible`, overlap → 409 `booking.doctor_busy`. Working hours are not required (staff decide). → 200 `AppointmentDto` (Confirmed, with history). Enqueues the patient notice and reminders |
| `POST …/appointments/{id}/reject` `{ reasonCode }` | `appointment.triage`. Pending only. `reasonCode` ∈ `NoDoctorAvailable`, `TimeUnavailable`, `ServiceUnavailable`, `PatientUnreachable`, `Other` (else 400). Releases the hold → 200 `AppointmentDto` |
| `POST …/appointments/{id}/cancel` `{ reasonCode }` | `appointment.cancel`. **Confirmed only** (Pending → 409 `booking.invalid_transition`; use reject). `reasonCode` ∈ `DoctorUnavailable`, `ClinicClosed`, `PatientRequest`, `ClinicEmergency`, `Other` → 200 |
| `POST …/appointments/{id}/reschedule` `{ startsAt, doctorId }` | `appointment.triage`. Confirmed only. Same duration; start on the grid (400), not in the past and within the horizon (422 `booking.lead_time`); doctor rules as confirm. Moves the block atomically; history row Confirmed → Confirmed `Rescheduled`; patient notice; old reminders cancelled, new ones enqueued → 200 |
| `POST …/appointments/{id}/complete` `{ chargedAmount: Money }` | `appointment.outcome` (Doctor: own). Confirmed only; from the start time (422 `booking.too_early_for_outcome`); amount ≥ 0, ≤ 2 decimals (400 `finance.amount_invalid`). Sets `chargedAmount`, releases the block, calls the BE3 completion hook in the same transaction → 200 |
| `POST …/appointments/{id}/no-show` | `appointment.outcome` (Doctor: own). Confirmed only; from start + 15 min (422) → 200 |
| `POST …/appointments` (walk-in) | `appointment.create_walkin` on the body's branch (other clinic's branch → 404). Body `{ branchId, serviceId, startsAt, doctorId, contactName, contactPhone }`, optional `Idempotency-Key`. Start on the grid, not earlier than now − 60 min, within the horizon (422 `booking.lead_time`); doctor eligible (422) and free (409 `booking.doctor_busy`). → **201** `AppointmentDto` (Confirmed, `source: 'WalkIn'`, no patient account, no notification on creation; BE4: a later clinic cancel / reschedule sends an SMS to the contact phone) |
| `GET …/calendar?branchId&date` | `appointment.view` on the branch (`branchId` required) → `CalendarDto` for the branch-local day: the branch's Pending (as `Hold`, not overdue), Confirmed, Completed, NoShow appointments in their doctor's column, time off, and busy time at other branches (`otherBranch: true`, no details). Doctor role: own column only, holds without details |
| `GET …/doctors/{doctorId}/time-off?from&to` | Members who share a branch with the doctor (or the doctor itself) → `TimeOffDto[]` overlapping [from, to); default: not yet ended |
| `POST …/doctors/{doctorId}/time-off` `{ startsAt, endsAt, reason? }` | `schedule.manage` on **every** branch the doctor is linked to, or the Doctor role for its own record. End after start and in the future, ≤ 90 days, start within a year, reason ≤ 200. Overlap with any active block (hold, appointment, other time off) → 409 `schedule.timeoff_conflict` (exclusion constraint). → **201** `TimeOffDto` |
| `DELETE …/doctors/{doctorId}/time-off/{id}` | Same rights → 204 (deactivated); unknown/removed → 404 |
| `GET …/dashboard` (BE2 counts) | `pending` = Pending requests waiting now (any date); `confirmed`/`completed`/`noShow` = appointments starting today (branch/clinic time). Doctor role: own appointments only |
| `PUT …/doctors/{doctorId}` (BE2 rule) | Removing a branch or service link that appointments or time blocks reference → 409 `schedule.link_in_use` (deactivate the doctor instead) |

### 4.3 Finance in the console (BE3, 2026-10-18)

Every handler loads the appointment/payment **within the route clinic** (other clinic → 404) and checks the
permission on **its** branch (no membership → 404; role or branch scope lacking → 403). Money is `Money`
(`{ amount: "1500000.00", currency: "UZS" }`, > 2 decimals → 400 `finance.amount_invalid`). Writes run in one
transaction with the ledger postings and the audit row (FINANCIAL_RULES §11).

```ts
type PaymentDto = { id; appointmentId; branchId; direction: 'In'|'Refund'; method: 'Cash'|'Card'|'Transfer'; amount: Money
  status: 'Recorded'|'Failed'; externalRef: string|null; refundOfPaymentId: string|null; recordedAt: string
  contactName: string|null; serviceName: string|null
  refunded: Money; refundable: Money; reason: string|null; failureReason: string|null; failedAt: string|null }   // last 5 additive
type Balance = { charged: Money; paid: Money; refunded: Money; outstanding: Money }  // outstanding < 0 = patient credit
type DailyReportRow = { date: 'YYYY-MM-DD'; branchId: string|null; visits: number; charged: Money; collected: Money
  refunded: Money; commission: Money; priorPeriodRefunded: Money }                                            // last additive
type MonthlyReport = { year; month; branchId: string|null; visits; charged; collected; refunded; commission; priorPeriodRefunded: Money
  reconciled: boolean; commissionRuleConfigured: boolean; locked: boolean }                                   // branchId, priorPeriodRefunded, locked additive
type SettlementDto = { id; clinicId; clinicName; year; month; status: 'Draft'|'Finalized'|'Paid'|'Voided'; amountDue: Money
  createdAt; finalizedAt; paidAt; paidReference; voidReason
  periodStart: 'YYYY-MM-DD'; periodEnd: 'YYYY-MM-DD' /* exclusive */; itemCount: number; commissionRuleConfigured: boolean; paymentTerms: string|null } // last 5 additive
```

| Endpoint | Contract as implemented |
|---|---|
| `POST …/appointments/{id}/payments` `{ amount, method, externalRef?, status? }` + **`Idempotency-Key` (required)** | `payment.record` (Owner, Receptionist, FinanceManager) on the appointment's branch. Appointment must be **Confirmed or Completed** (else 422 `finance.appointment_not_payable`); amount > 0; `externalRef` ≤ 100. Overpayment/prepayment is allowed (patient credit). **Additive** `status: 'Failed'` records a declined attempt with **no ledger effect** (F10). → **201** `PaymentDto`; replay → same 201 body; same key + other body → 422 `common.idempotency_mismatch`; the key is also UNIQUE per clinic (another user's key → 422). Missing key → 400 `common.validation` (`errors["Idempotency-Key"]`) |
| `POST …/payments/{paymentId}/fail` `{ reason 1–500 }` | `payment.record`. Recorded incoming payment without refunds → Failed; reversal entries (payment + its commission) in the current open period, originals untouched (F11 correction = fail + new payment). Wrong state (already failed, a refund, has refunds) → 409 `finance.invalid_payment_state` → 200 `PaymentDto` |
| `POST …/payments/{paymentId}/refund` `{ amount, reason 1–500 }` + **`Idempotency-Key` (required)** | `payment.refund` (Owner, FinanceManager — **not** Receptionist). Recorded incoming payment only (409 `finance.invalid_payment_state`); Σ refunds ≤ payment amount (422 `finance.refund_exceeds_paid`, nothing written). Same method as the original. → **201** `PaymentDto` (`direction: 'Refund'`) |
| `GET …/payments?branchId&appointmentId&from&to&page&pageSize` | `payment.record` **or** `finance.report.view`; rows of the caller's branches only (SQL). **`appointmentId` filter added** (FRONTEND §9.2-2). `from`/`to` = UTC instants, half-open `[from, to)` on `recordedAt`; `from ≥ to` → 400. Branch/appointment outside scope → 403, other clinic's → 404. `Page<PaymentDto>` newest first |
| `GET …/appointments/{id}/balance` | `payment.record` or `finance.report.view` → `Balance`. `charged` = amount entered at completion (0 before), `paid` = recorded incoming payments, `refunded` = refunds, `outstanding` = ledger PatientReceivable of the appointment (negative = credit) |
| `GET …/reports/daily?branchId&from&to` | `finance.report.view` (Owner, FinanceManager; Branches-scoped managers: their branches) **or platform admin with `platform.finance.settle`**. `from`/`to` = **branch-local dates `YYYY-MM-DD`, inclusive**, ≤ 92 days (`Finance:DailyReportMaxDays`). One row per day (zero rows included), summed over the scope's branches (`branchId: null`) or one branch. Computed from the ledger, not the cache |
| `GET …/reports/monthly?year&month&branchId` | Same rights. Totals of the branch-local month; `reconciled` = cached daily summaries equal the ledger for every (branch, day) of the month (false until the nightly job covered it); `commissionRuleConfigured` = a rule overlaps the month (F14 flag); `locked` = Finalized/Paid settlement |
| `GET …/settlements` | `finance.settlement.view` (clinic-wide: Owner, Clinic-scoped FinanceManager) or `platform.finance.settle` → `SettlementDto[]` (unpaginated, ≤ 500, newest period first, Voided included) |
| `GET …/dashboard` (BE3 field) | `collected` = today's **net** collected (payments − failed reversals − refunds) of the dashboard's branches, from the ledger; only with `finance.report.view`, else `null` |

Figures: `charged` = gross revenue at completion; `collected` = money in net of failed-payment reversals; `refunded` =
money returned; `priorPeriodRefunded` = refunds of payments posted in an earlier month (F9); `commission` = net
platform commission (accruals − reversals). A refund first returns the patient's credit on the appointment; the rest is
a price reduction (contra-revenue) — see FINANCIAL_RULES §11.

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
audit; BE2: reports, reviews, break-glass appointment read (§5.3); BE3: commission rules and settlements (§5.4);
BE4: notifications (§5.5).
Every platform check answers 403 `common.forbidden` without the grant and 403 `auth.totp_setup_required`
while TOTP is not enabled (`Security:RequireTotpForPrivilegedRoles`). Permission split (as the UI
assumed): approve/reject = `platform.clinic.verify`; suspend/reinstate = `platform.account.suspend`.
Note for BE2: the break-glass header `X-Access-Reason` arrives **percent-encoded (UTF-8)** from the UI and
must be decoded by the server.

```ts
type AdminDashboardDto = { clinicsByStatus: Record<ClinicVerification, number>; appointmentsToday: number /* BE2: starting today, Asia/Tashkent */
                           openReports: number /* BE2 */; activeUsers: number; suspendedUsers: number }
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

### 5.3 Reviews, reports and break-glass (BE2, 2026-10-18)

```ts
type AdminReviewDto = { id; clinicId; clinicName; branchName; appointmentId; rating: number; text: string; authorName: string
                        status: 'Pending'|'Published'|'Hidden'; moderationReason: string|null; createdAt }
type AbuseReportDto = { id; targetType: 'Clinic'|'Review'; targetId; targetLabel: string|null /* clinic name or review excerpt */
                        clinicId; clinicName; reason; status: 'Open'|'Resolved'; createdAt; resolution: string|null; resolvedAt: string|null }
```

| Endpoint | Contract as implemented |
|---|---|
| `GET /admin/reviews?status&clinicId&page&pageSize` | `platform.reports.handle` → `Page<AdminReviewDto>`, Pending first, then newest |
| `POST /admin/reviews/{id}/publish` · `/hide { reason 1–500 }` | `platform.reports.handle` → 204. Pending/Hidden → Published, Pending/Published → Hidden; same status → 409 `admin.invalid_status_transition`. Recomputes the clinic's `rating`/`reviewCount` over Published reviews (serialised per clinic). Audited |
| `GET /admin/reports?status&page&pageSize` | `platform.reports.handle` → `Page<AbuseReportDto>`, Open first |
| `POST /admin/reports/{id}/resolve { resolution 1–1000 }` | → 204; already resolved → 409 `admin.invalid_status_transition`. Audited |
| `GET /admin/clinics/{id}/appointments?branchId&status&from&to&doctorId&page&pageSize` + `X-Access-Reason` | `platform.tenant-data.read` (403 first). The header is **percent-encoded UTF-8** (`encodeURIComponent`) and decoded by the server; after trimming 15–300 characters, no control characters, else 400 `common.validation` (`errors["X-Access-Reason"]`). Unknown clinic → 404. → `Page<AppointmentDto>` (all branches, full contact data, `canCancel: false`). Every successful read writes audit `platform.tenant_data.read` with the decoded reason, the filters and the row count |

### 5.4 Commission rules and settlements (BE3, 2026-10-18)

All need `platform.finance.settle` (403 `common.forbidden` without it, 403 `auth.totp_setup_required` while TOTP is
not enabled). Every write is audited (`finance.commission_rule.*`, `finance.settlement.*`).

```ts
type CommissionRuleDto = { id; clinicId; basis: 'CollectedPayment'|'CompletedService'|'ConfirmedAppointment'|'NewPatient'
  rate: string|null /* "0.0750" = 7.5 %, 4 decimals */; fixedFee: Money|null; effectiveFrom: string; effectiveTo: string|null
  contractReference: string; rounding: 'AwayFromZero'; approvedByUserId: string; createdAt: string; endedAt: string|null } // clinicId, rounding, approvedByUserId, endedAt additive
```

| Endpoint | Contract as implemented |
|---|---|
| `GET /admin/clinics/{id}/commission-rules` | → `CommissionRuleDto[]` by basis, start. Unknown clinic → 404 |
| `POST /admin/clinics/{id}/commission-rules` `{ basis, rate?, fixedFee?, effectiveFrom, effectiveTo?, contractReference 1–200 }` | Percentage bases (`CollectedPayment`, `CompletedService`) need `rate`: a **fraction string** in (0, 1] with ≤ 4 decimals, digits and `.` only (`"0.075"`, `"0.0750"`; `"7.5"`, `"1e-2"`, `"-0.1"` → 400); no `fixedFee`. Fixed-fee bases (`ConfirmedAppointment`, `NewPatient`) need `fixedFee > 0`, no `rate`. `effectiveTo > effectiveFrom`. Range `[from, to)` overlapping a rule of the same clinic + basis → **409 `finance.rule_overlap`** (adjacent ranges allowed). The approver is the caller. → **201** |
| `POST /admin/clinics/{id}/commission-rules/{ruleId}/end` `{ effectiveTo }` | Only shortens: after the start, not later than the current end, **not in the past** (database time; postings already made with the rule are immutable) → 400 `common.validation` otherwise. → **200** `CommissionRuleDto` (UI treats it as void) |
| `GET /admin/settlements?clinicId&status` | → `SettlementDto[]`, unpaginated (≤ 500), newest period first |
| `POST /admin/settlements` `{ clinicId, year 2020–2100, month 1–12, paymentTerms? ≤ 500 }` | The month (Asia/Tashkent) must have ended (400 `common.validation` on `month`). Creates a **Draft** with every unsettled commission line of the clinic posted in the month (`amountDue` may be 0 → see `commissionRuleConfigured`). A non-voided settlement for the month exists → **409 `finance.settlement_exists`** (also under concurrency). Unknown clinic → 404. → **201** |
| `POST /admin/settlements/{id}/finalize` | Draft → Finalized; adds lines of the month posted since the draft, then **locks the month** → 200 `SettlementDto` |
| `POST /admin/settlements/{id}/mark-paid` `{ paidAt, reference 1–200 }` | Finalized → Paid (`paidAt` not later than now + 1 day); posts Dr SettlementClearing / Cr PlatformCommissionReceivable → 200 |
| `POST /admin/settlements/{id}/void` `{ reason 1–500 }` | Draft → Voided, items released. Finalized/Paid → **409 `finance.period_locked`** → 200 |
| | Other wrong transitions → 409 `finance.invalid_settlement_state`; unknown id → 404 |

### 5.5 Notifications (BE4, 2026-10-18)

Both need `platform.audit.view` (403 `common.forbidden` without it, 403 `auth.totp_setup_required` while TOTP is not
enabled). Message bodies and payloads are **never** returned (they contain names/times; OD-10).

```ts
type NotificationStatus = 'Pending'|'Processing'|'Sent'|'Failed'|'Cancelled'|'Skipped' // UI assumed Pending|Sent|Failed (subset)
type AdminNotificationDto = { id; type: string /* appointment.confirmed.patient … */; audience: 'patient'|'staff'
  channel: string|null /* 'Telegram' | 'Sms' | 'Telegram,Sms' of the last attempt */; status: NotificationStatus; attempts: number
  createdAt; notBefore; nextAttemptAt: string|null /* only while Pending */; sentAt: string|null
  lastErrorCode: string|null /* telegram.http_500, telegram.blocked, sms.failed, notify.no_channel, reminder.stale … */
  lastError: string|null /* scrubbed provider description, never text/phones */ }
```

| Endpoint | Contract as implemented |
|---|---|
| `GET /admin/notifications?status&page&pageSize` | → `Page<AdminNotificationDto>`, newest first. Unknown `status` → 400 |
| `POST /admin/notifications/{id}/retry` | Failed → Pending with a fresh attempt budget (attempts 0), due now → **204**; audit `admin.notification.retried`. Other status → 409 `admin.invalid_status_transition`; unknown id → 404 |

Deviation from the UI assumption (`api/types.ts` `NotificationDto`): additive fields `audience`, `notBefore`, `sentAt`,
`lastErrorCode`; the status union is larger.

## 6. Telegram

| Method & path | Auth | Purpose |
|---|---|---|
| `POST /telegram/webhook/{secretPath}` | Telegram (secret path + `X-Telegram-Bot-Api-Secret-Token`) | Bot updates when `Telegram:Mode=Webhook`. In `Polling` mode (default for local work) the Worker calls `getUpdates` instead |
| `GET /telegram/config` | anon | `{ enabled, botUsername, miniAppUrl, botId }` for the UI. `enabled` is true only when `Telegram:Enabled` and a bot token + username are configured; the others are null otherwise |

Bot behaviour: `/start <linkToken>` links the chat to the account that created the token. `/start`
alone sends a welcome message with a "Open OqTish" Mini App button. `/stop` unlinks.
`/appointments` lists upcoming appointments for linked patients.

### 6.1 Implementation notes (BE4, 2026-10-18)

| Endpoint | Contract as implemented |
|---|---|
| `POST /telegram/webhook/{secretPath}` | Mapped always; answers only when Telegram is enabled **and** `Telegram:Mode=Webhook`. `secretPath` = first 32 hex chars of SHA-256(`"oqtish-webhook-path:" + Telegram:WebhookSecret`) (the API registers it itself with `setWebhook` on startup, using `Telegram:WebhookBaseUrl`). Wrong path / Polling mode / disabled → **404** `common.not_found`; right path with a missing or wrong `X-Telegram-Bot-Api-Secret-Token` → **401** `telegram.webhook_unauthorized` (constant-time comparison). Body = a Bot API `Update`; after authentication always **200** (bad JSON or a failing update are logged by type only, so Telegram does not redeliver forever). No `X-OqTish-Client` needed. Anonymous (no cookie) |
| `GET /telegram/config` | **Additive** `botId: number\|null` — the numeric part of the bot token, for the Login Widget popup (FRONTEND §9.2 item 6) |

Bot behaviour as implemented (docs/SERVICES.md §5.5): private chats only (groups/channels/bots ignored, OD-10); per-chat
flood limit (`Telegram:ChatRateLimitPerMinute`, 20); language from the sender's `language_code` (uz/ru/en, fallback uz).
`/start <token>` → `ConsumeTelegramLinkTokenAsync` (invalid/used/expired → explanation; Telegram id owned by another
account → refusal, no merge). `/start` → re-activates a stopped/blocked link, or links the chat of a user who already
signed in with that Telegram account (Mini App / Login Widget), else a welcome text with the "OqTish'ni ochish" web_app
button. `/appointments` → the linked patient's own upcoming Pending/Confirmed appointments (max 5). **Deviation:** `/stop`
**deactivates** the chat link (no more bot messages; important ones fall back to SMS) but keeps the Telegram sign-in
identity — full unlinking stays `DELETE /me/telegram`. `/help` → command list.

## 7. Development-only — `/api/dev` (404 outside Development)

| Method & path | Purpose |
|---|---|
| `GET /dev/sms?phone` | Messages captured by the Fake SMS sender (to read OTP codes during local testing) → `[{ text, sentAt }]`, newest first. BE4: also SMS fallback messages sent by the Worker (from `notify.dev_messages`) |
| `GET /dev/telegram?chatId` | Messages the Fake Telegram client would have sent (by the API **and** the Worker — captured in `notify.dev_messages`), newest first, max 100 → `[{ chatId, text, buttons /* inline_keyboard JSON or null */, sentAt, origin }]` (BE4) |
| `POST /dev/telegram/simulate` `{ telegramUserId, text, languageCode? }` | BE4: feeds a private-chat message to the bot as if Telegram sent it (e.g. `/start <token>` from a deep link) → `{ outcome }` (`Linked`, `LinkInvalid`, `Welcome`, `Appointments`, …); the reply appears in `/dev/telegram`. 400 without `telegramUserId`/`text` |
| `POST /dev/seed` | Idempotently loads fictional demo data (same as `dotnet run --project src/OqTish.Api -- seed`, which is allowed in Development and Staging and always refused in Production) → `{ contributors: string[] }`. Identity seed: admin `admin@oqtish.local` / `Seed:AdminPassword` (dev-only default `Admin123!demo`) with all platform permissions; demo patient `+998900000001`. BE1b adds 3 approved demo clinics in Tashkent + 1 Pending, services, doctors, hours and demo staff logins for every role (`Seed:DemoStaffAccounts`, Development only) — credentials in docs/SERVICES.md §2.9. BE2 adds demo appointments in every status for the first clinic, one published and one pending review and an open abuse report (SERVICES.md §3.12) |

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
| `telegram.webhook_unauthorized` | 401 | Webhook call without the right `X-Telegram-Bot-Api-Secret-Token` (BE4) |
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
| `schedule.timeoff_conflict` | 409 | Time off overlaps appointments, holds or other time off |
| `staff.cannot_grant_role` | 403 | Escalation attempt |
| `staff.last_owner` | 422 | Cannot remove the last owner |
| `staff.already_member` | 409 | The phone/user is already a member of the clinic (BE1b) |
| `staff.invitation_expired` | 422 | Invitation past its 7-day expiry (BE1b) |
| `clinic.registration_limit` | 422 | Too many Pending clinic registrations for this user (BE1b) |
| `schedule.hours_overlap` | 422 | Working-hour intervals overlap on a weekday (BE1b) |
| `schedule.branch_not_linked` | 422 | Working hours for a branch the doctor is not linked to (BE1b) |
| `schedule.doctor_user_invalid` | 422 | Doctor `userId` is not a member of the clinic (BE1b) |
| `schedule.doctor_user_taken` | 409 | The user is already linked to another doctor of the clinic (BE1b) |
| `schedule.link_in_use` | 409 | A doctor's branch/service link is referenced by appointments or time blocks and cannot be removed (BE2) |
| `admin.invalid_status_transition` | 409 | Clinic/account not in a state allowing the action (BE1b) |
| `admin.cannot_suspend_self` | 422 | A platform admin cannot suspend their own account (BE1b) |
| `finance.refund_exceeds_paid` | 422 | Refund larger than the payment's net paid amount |
| `finance.amount_invalid` | 400 | Amount ≤ 0 or > 2 decimals |
| `finance.period_locked` | 409 | The month is locked by a Finalized/Paid settlement (void refused). Postings dated into a locked month are not errors: they are redirected to the next open month (BE3) |
| `finance.appointment_not_payable` | 422 | Payment for an appointment that is not Confirmed/Completed (BE3) |
| `finance.invalid_payment_state` | 409 | Fail/refund on a payment that is not a recorded incoming payment (or fail with refunds) (BE3) |
| `finance.invalid_settlement_state` | 409 | Settlement transition not allowed from its status (BE3) |
| `finance.settlement_exists` | 409 | Settlement already exists for the period |
| `finance.rule_overlap` | 409 | Commission rule overlaps an existing one |
| `review.not_eligible` / `review.already_exists` | 422 / 409 | Review rules |
