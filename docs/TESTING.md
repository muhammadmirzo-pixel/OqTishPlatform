# Testing Strategy and Required Test Catalogue

Status: Phase 2 harness in place. The catalogue below (§2–§6) is the required target; §0 lists what exists today (BE1a §0.1, BE1b §0.2, BE2 §0.3, BE3 §0.4 and BE4 §0.5 — written, not yet executed).

## 0. Current tests (Phase 2, verified 2026-10-18)

| Suite | Test | Proves |
|---|---|---|
| ArchitectureTests | `Domain_depends_on_no_other_layer_or_framework` | Domain has no EF/ASP.NET/Npgsql/other-layer references |
| ArchitectureTests | `Application_does_not_depend_on_infrastructure_or_hosts` | layering |
| ArchitectureTests | `Infrastructure_does_not_depend_on_hosts` | layering |
| ArchitectureTests | `Domain_and_application_never_use_binary_floating_point` | no float/double (money rule) |
| IntegrationTests | `All_migrations_apply_to_a_clean_database` | R2: migrations on an empty PostgreSQL 18 |
| IntegrationTests | `Required_postgres_extensions_are_installed_by_migrations` | btree_gist + citext present (needed for ADR-004) |
| IntegrationTests | `Model_has_no_changes_missing_from_migrations` | nobody forgot `migrations add` |
| IntegrationTests | `App_roles_get_dml_on_owner_objects_but_no_ddl` | runs the real `deploy/postgres/init` script; api/worker roles can INSERT/SELECT but CREATE/ALTER/DROP are denied (42501) |
| IntegrationTests | `Ready_is_healthy_when_database_is_migrated` | R4 |
| IntegrationTests | `Ready_is_unhealthy_while_migrations_are_pending` | readiness really fails (503) |
| IntegrationTests | `Unknown_route_returns_problem_details_without_internals` | ProblemDetails, no exception text (Production) |
| IntegrationTests | `Api_documentation_is_not_exposed_in_production` ×2 | OpenAPI/Swagger 404 in Production |
| web (Vitest) | `api/client.test.ts` (14) | HTTP status → error kind mapping, trace id, non-JSON errors, network errors, same-origin cookies |
| web (Vitest) | `config.test.ts` (2) | runtime config + fallbacks |
| web (Vitest) | `App.test.tsx` (2) | readiness indicator: ready / unavailable |

### 0.1 Added in BE1a (identity, common, tenancy schema) — verified 2026-10-18

`dotnet test`: **166 passed** (ArchitectureTests 6, UnitTests 75, IntegrationTests 85).

| Suite / class | Proves |
|---|---|
| Architecture `LayerDependencyTests` | Domain references only the BCL; Application may use EF Core (core assemblies only) but not Npgsql, ASP.NET Core, Identity, Relational or Infrastructure; no float/double in members **or method signatures**; `Money` is decimal-backed |
| Unit `MoneyTests`, `PageRequestTests` | rounding (2 dp, AwayFromZero), currency guards, JSON `{ amount: "x.xx", currency }`, strict parsing (≤ 2 decimals, no exponent, UZS only → `MoneyFormatException`), paging bounds |
| Unit `PhoneNumberTests` | Uzbek normalisation (accepted/rejected forms), masking |
| Unit `TotpTests` | **RFC 6238 SHA-1 test vectors**, ±1 step window, replay of used/older steps rejected, base32 (RFC 4648), otpauth URI |
| Unit `TelegramLoginVerifierTests` | Mini App initData valid / tampered / other bot / stale (>24 h) / garbage; Login Widget valid / tampered / stale / wrong key derivation |
| Unit `EskizSmsSenderTests` | Eskiz login + send form fields (fake HttpMessageHandler), token cached, refresh once on 401, errors → `SmsDeliveryException` (**not verified against live Eskiz**) |
| Unit `StartupGuardsTests` | Fake SMS refused in Production; missing/short/dev pepper refused outside Development; Eskiz needs credentials |
| Unit `PermissionsSnapshotTests` | role → permission map equals `Snapshots/role-permissions.txt` (SECURITY.md §8) |
| Unit `AccessScopeTests` | not-found / forbidden / branch scope / clinic-wide-only / TOTP block / doctor own-only / platform grants |
| Integration `OtpSignInTests` | happy path (patient + session + audit), `__Host-oqtish` cookie attributes, account reuse, wrong / expired / 6th attempt / reuse of code, resend cooldown + Retry-After, 3-per-phone and per-IP DB limits, ASP.NET limiter burst, **identical responses for known/unknown numbers**, SMS failure invisible, phone validation |
| Integration `PasswordAndTotpTests` | password login, identical wrong/unknown answers, lockout, **TOTP required / invalid / valid / replay**, OTP sign-in requires TOTP, enabling TOTP revokes other sessions, secret encrypted at rest, password change rules, e-mail conflict, password reset (decoy vs real, revokes sessions) |
| Integration `SessionTests` | logout, logout-all, list/revoke own sessions (other user's → 404), **suspended user → 401 on the next request**, suspended user cannot sign in, DB-revoked and 14-day-idle sessions rejected |
| Integration `AccountLinkingTests` | **phone owned by another account → 409 `auth.identity_in_use`, no merge, audit row**; LinkPhone challenge bound to its account; Telegram deep-link token consumed once (`ConsumeTelegramLinkTokenAsync`), conflict, unlink, last-identity guard |
| Integration `TelegramSignInTests` | Mini App sign-in creates one patient; tampered / stale / other-bot initData → 401; widget hash valid / tampered |
| Integration `MeAndAccountTests` | profile, self-deletion (anonymised, signed out, phone reusable, staff blocked), effective permissions in `/me`, seed idempotent + admin login, seed command refused in Production |
| Integration `LogRedactionTests` (S16) | captured logs (all levels) of OTP, password, Telegram, link and logout flows contain no phone, code, cookie, password, initData/hash, bot token or link token |
| Integration `AccessServiceTests` | **cross-clinic → NotFound, out-of-branch → Forbidden, suspended clinic / membership / user → NotFound**, clinic vs branch scope, doctor own-only, **TOTP rule** (blocked → `auth.totp_setup_required`, `/me` shows empty permissions, allowed after enabling) |
| Integration `SecurityPipelineTests` | **CSRF header missing/unknown → 403** (before auth; GET exempt; `tg-miniapp` accepted), **`/api/dev/*` → 404 in Production**, dev SMS mailbox in Development, **Production refuses Fake SMS / missing pepper at startup**, malformed JSON → `common.validation`, Telegram config/disabled |
| Integration `EndpointAuthorizationTests` (S17) | every endpoint has AllowAnonymous or an authorization requirement (EndpointDataSource reflection; checker proven to flag a bare endpoint); **every protected endpoint answers 401 to an anonymous caller**; forged cookie → 401 |
| Integration `CommonPortsTests` | runner commits on success and rolls back (incl. audit + outbox) on failure; 40001/40P01 retried 3× then `common.concurrency`; other errors not retried; classifier returns constraint names; row locks require a transaction, branch lock row exists and blocks a concurrent `FOR UPDATE` (55P03); outbox dedup; **idempotency replay / mismatch / 4 concurrent duplicates → one execution** |
| Integration `MigrationTests` (+3) | latest migration Down → Up; audit log UPDATE/DELETE/TRUNCATE blocked even for the owner; composite FKs reject a cross-clinic branch grant; branch-lock trigger |
| Integration `DatabaseRoleTests` (extended) | `oqtish_api`/`oqtish_worker` can INSERT/SELECT `audit.audit_log` but UPDATE/DELETE are denied (42501) |

### 0.2 Added in BE1b (clinic console, catalogue, scheduling, public, admin) — verified 2026-10-18

`dotnet test`: **194 passed** (ArchitectureTests 6, UnitTests 75, IntegrationTests 113; +28 integration tests in BE1b), 0 warnings.

| Suite / class | Proves |
|---|---|
| Integration `ClinicProfileAndBranchTests` | registration → caller is ClinicOwner of a Pending clinic, **hidden publicly (404)**, audited; validation + 3-pending limit; profile read (membership) / edit (clinic-wide, receptionist & branch admin 403, other clinic 404); dashboard counts 0, `collected` only with finance permission, out-of-branch 403 / other clinic 404; branch list filtered by scope; create/update with **cross-clinic id substitution → 404**, out-of-branch 403, wrong role 403, validation (lat/lng pair, time zone); hours replace + overlap/format validation; clinic audit owner-only |
| Integration `CatalogAndDoctorTests` | services: clinic-wide `catalog.manage`, unknown category 400, cross-clinic service/branch ids 404; offers: branch-scoped admin own branch 204 / other 403, price ≥ 0, > 2 decimals → `finance.amount_invalid`, duration 5..480, list offers filtered by scope; doctors: branch-scoped receptionist create/update per branch (403 outside), other-clinic ids 400/404, list filtered; **userId** needs `staff.manage`, must be a member (422), unique (409); **Doctor-own resolver** (`DoctorIdFor`); working hours: replace, **overlap across branches → 422**, unlinked branch 422, scoped edit keeps other branches, unlink cascades; DB: **exclusion constraint 23P01** (adjacent OK), composite FKs reject cross-clinic doctor_branches / branch_services (23503) |
| Integration `StaffAndInvitationTests` | staff list scoped (branch admin sees only memberships inside its branches), receptionist 403, other clinic 404; **escalation attempts** (admin → ClinicAdmin/Owner, admin edits/removes owner, self-change, branch admin grants Clinic scope or foreign branch) → 403 `staff.cannot_grant_role`; allowed changes audited; **last owner** 422; **removal effective immediately** (next request 404); invitation by phone: replace, masked phone, 7-day expiry, **phone mismatch → 404**, accept creates Branches membership, already member 409, removed member reactivated; **expired → 422**, revoked → 404; invitation escalation rules; no phone in audit details, only HMAC stored |
| Integration `PublicCatalogTests` | over the demo seed: only approved clinics; filters `q` (clinic + service name, case-insensitive), `serviceCategory`, `serviceId`, `district`; sort name/price/rating; **Haversine distance** order and value; pagination; validation (distance without coordinates, lat without lng, ranges, pageSize 101, unknown sort/category); detail with hours/offers, pending/unknown → 404, inactive branch/offer hidden; **suspended clinic hidden publicly and its staff → 404**, reinstate restores; facets |
| Integration `AdminTests` | dashboard (any platform permission, patient 403); clinic verification with the **permission split** (verify vs suspend), invalid transitions 409, reason required, unknown 404, every action **audited**; **suspension disables memberships** (`/me` and console) and reinstate restores; TOTP rule → `auth.totp_setup_required` until enabled; user suspension → **sessions revoked, next request 401**, self 422, reinstate; admin audit filters (action prefix, clinic); **`create-platform-admin` CLI in Production**: weak passwords / argv password / missing phone → exit 1, success + idempotent re-run (password not reset, all grants, verified phone, audited), other account's phone → exit 3, admin can sign in |
| Integration `DemoSeedTests` | **seed twice → identical row counts** (4 clinics, 5 branches, 30 opening hours, 24 services, 30 offers, 7 doctors, 6 memberships …); every demo staff login works with its documented password and has the expected role; demo doctor account resolves to its doctor record; receptionist is Branches-scoped |
| Integration `EndpointAuthorizationTests` (unchanged) | covers every new endpoint automatically (auth metadata + anonymous 401) |

### 0.3 Added in BE2 (booking, reviews, reports) — verified 2026-10-18

`dotnet test`: **300 passed** (ArchitectureTests 6, UnitTests 149, IntegrationTests 145), 0 warnings; full run
1 min 10 s on an idle machine, ~3 min under heavy host load. Concurrency tests carry `[Trait("Category", "Concurrency")]`
and run in the default `dotnet test`; each loops **50 iterations** over a fresh fixture (new clinic, branch,
doctors, patients in the same database), releases the racing calls through a barrier (`Race`: every participant
opens its DI scope and DB connection first, then waits on one gate), and asserts DB state. They call the real
handlers through `BookingHost` (AddInfrastructure DI graph, real transactions/AccessService/PostgreSQL, caller set
per scope); contracts and authorization are covered over HTTP with real sign-in.

| Id | Test (class) | Proves |
|---|---|---|
| B1 | `B1_twenty_concurrent_requests_for_one_doctor_slot_give_one_hold` (Concurrency) | 20 parallel requests, 1 doctor → exactly 1 Pending, 19 × 409 `booking.slot_unavailable`, 1 active block (50×) |
| B1x | `B1_cross_branch_requests_and_walk_ins_for_a_shared_doctor_never_double_book` | 10 patient requests at branch 1 (doctors A, B) vs 10 walk-ins for A at branch 2 (different branch locks) → exactly 2 bookings on different doctors; with the exclusion constraint dropped (manual experiment) 12 succeeded — the constraint is what prevents it (50×) |
| B2 | `B2_overlapping_30_and_60_minute_requests…` | [10:00,10:30) vs [10:15,11:15) on one doctor → exactly one wins (50×) |
| B3 | `B3_adjacent_intervals…` (Rules) | [10:00,10:30) + [10:30,11:00) concurrently → both succeed (half-open periods) |
| B4 | `B4_two_doctors_three_concurrent…` | 3 requests, 2 doctors → 2 succeed, blocks on both doctors (50×) |
| B5 | `B5_least_flexible_doctor_is_held_first…` | A{X,Y}, B{X}: X held on B, Y on A; plus a documented greedy limit (A{X,Y}, B{X,Z} → later Y gets 409) |
| B6 | `B6_two_staff_confirm_the_same_request…` | 2 staff confirm with different doctors → 1 Confirmed, 1 × 409 `booking.not_pending`, 1 history row, 1 confirmed outbox message (50×) |
| B7 | `B7_confirm_racing_the_sweeper_at_the_deadline…` | deadline set 25 ms ahead in DB time, confirm vs Worker sweep handler → final status ∈ {Confirmed, Expired} consistent with the confirm result, exactly 2 history rows, 1 notice, block active iff Confirmed (50×; both outcomes observed on an idle machine, e.g. 40/10; all Expired under heavy load). This test found the confirm self-sweep bug (DECISIONS.md BE2-4) |
| B8 | `B8_confirm_after_the_deadline_with_the_sweeper_stopped…` | late confirm → 409 `booking.expired`, DB still Pending, patient/staff reads and `status` filters show Expired, cancel → 409; one later sweep expires it and enqueues one notice |
| B9 | `B9_patient_cancel_racing_staff_confirm…` | always ends CancelledByPatient; history is either Pending→Confirmed→Cancelled or Pending→Cancelled with the confirm getting 409 (both orders exercised, 50×) |
| B10 | `B10_time_off_racing_a_confirm_onto_the_same_doctor…` | exactly one of {confirm, time off} commits (both orders exercised, 50×) |
| B11 | `B11_failure_injected_after_the_block_insert…` | a trigger raising after the block insert → nothing persists (appointment, block, history, outbox, audit, idempotency key) and the key is reusable |
| B12 | `B12_concurrent_duplicates_with_one_idempotency_key…` + `B12_same_key_sequential…` | 5 concurrent duplicates → one appointment, same id for all (50×); sequential replay, different body → 422 `common.idempotency_mismatch` |
| B13 | `B13_reject_expire_and_cancel_release_the_block…` | reject / sweeper expiry / patient cancel / clinic cancel each release the block and the slot is rebookable |
| B14 | `B14_an_overdue_pending_request_does_not_block…` | with the sweeper off, a new booking (and a doctor's time off) succeeds; the overdue request becomes Expired (System history, outbox notice) |
| B15 | `B15_outside_hours_shift_end_time_off_inactive…` | outside hours / across shift end / during time off / branch closed → 422 `booking.outside_hours`; off-grid → 400; lead time and horizon → 422 `booking.lead_time`; 4th Pending → 422 `booking.too_many_pending`; inactive offer/service/branch, Pending/Suspended clinic → 422 `booking.clinic_unavailable`; unknown branch → 404 |
| B16 | `B16_branch_local_time_near_utc_midnight…` | 00:15 Tashkent = 19:15Z of the previous day: listed under the local date, not the previous one, stored UTC, calendar on the right day |
| B17 | `B17_forced_deadlock_is_retried_at_most_three_times_then_409` | trigger raising 40P01: exactly 3 attempts then 409 `common.concurrency`, nothing persisted; one forced deadlock → success on the 2nd attempt |
| B18 | `BookingInvariants.AssertAsync` (called at the end of every booking test) | 0 overlapping active blocks per doctor; every active block ↔ Pending (Hold) / Confirmed (Appointment, same doctor/period/branch) / TimeOff; every Pending/Confirmed has exactly one active block; last history row = status |
| — | `Pending_limit_per_patient_holds_across_branches_under_concurrency` | one patient, 6 parallel requests at 2 branches → exactly 3 Pending (without the advisory lock, manual experiment: 4) (50×) |
| — | `Concurrent_moderation_of_two_reviews…` (20×) | two reviews published at once → exact rating 3.50 / 2 (without the clinic row lock, manual experiment: 2.00 / 1) |

Other BE2 tests:

| Suite / class | Proves |
|---|---|
| Unit `AppointmentStateMachineTests` | every (status × action) pair: exactly the 9 documented transitions; terminal states; factories and review/report moderation transitions |
| Unit `AvailabilityEngineTests` | slots follow working hours and never cross a shift end; holds, time off, lead time, opening hours; one free doctor suffices; candidate order; UTC ↔ local (UTC+5), grid, ISO weekday; break-glass reason decoding; reason codes (UI + brief); review author label |
| Integration `BookingApiTests` | patient flow over HTTP (availability, 201 without doctor, replay/mismatch, own lists/detail with history, other patient 404, cancel); **authorization matrix**: Branches-scoped receptionist (other branch 403), other clinic (404, also by id substitution), Doctor own-only (Pending invisible, after confirm visible), FinanceManager read-only (every transition 403), wrong-clinic/unlinked doctor 422; reject/cancel/reschedule/complete/no-show rules and errors; walk-in (Confirmed, idempotent, busy 409, scope 403/404, past 422); reminders enqueued/cancelled on reschedule; calendar + time off rights (schedule.manage on every branch, doctor own); dashboards (clinic, branch, doctor own, admin); `schedule.link_in_use`; self-deletion cancels and anonymises; booking rate limit 429 per session |
| Integration `ReviewReportAndBreakGlassTests` | review eligibility (not completed, other patient, window 30 days, once), validation, moderation rights (403 without `platform.reports.handle`), publish/hide transitions, public list and clinic rating recompute; abuse reports (idempotent per reporter/target, 404 for non-public targets, resolve once, dashboard count); break-glass (403 without grant, 400 without/short reason, 404 unknown clinic, percent-encoded Uzbek/Cyrillic reason decoded and audited) |
| Integration `ExpirySweeperServiceTests` | the Worker `BackgroundService` expires an overdue request on its timer and leaves a fresh one alone |
| Integration `MigrationTests.Booking_schema_…` | S18 composite FK (appointment with another clinic's branch → 23503), CHECKs (doctor required, Pending deadline, source/patient, block kind refs, interval), exclusion 23P01 and adjacent periods allowed, one block per appointment, history UPDATE/DELETE/TRUNCATE → 42501 |
| Integration `DatabaseRoleTests` (extended) | `oqtish_api`/`oqtish_worker` cannot UPDATE/DELETE `booking.appointment_status_history`, can `SELECT … FOR UPDATE` appointments |
| Integration `DemoSeedTests`, `PublicCatalogTests` (updated) | seed twice → identical counts incl. 10 appointments (all 8 statuses), 3 active blocks, 2 reviews, 1 report; demo clinic rating 5.00/1, `nextFreeSlot` set, `availableWithinDays=7` filter |
| Integration `EndpointAuthorizationTests` (unchanged) | covers all 28 new endpoints automatically (auth metadata, anonymous 401) |

Test infrastructure: `ApiFactory` (real `Program`, https client with `X-OqTish-Client`, log capture, per-test database cloned from a migrated template), `TestAuth` (`SignInAsPatientAsync` via the Fake SMS mailbox, `SignInAsStaffAsync` = real sign-in + membership inserted in the DB, `CreateClinicAsync`, `EnableTotpAsync`, `AssertProblemAsync`), `TelegramSigning`. Test hosts disable config reload-on-change (inotify limit).

Commands (from the repository root):

```bash
export DOCKER_HOST=unix:///var/run/docker.sock   # only if Docker Desktop is installed but not running
dotnet test                                      # 166 tests; starts postgres:18-alpine via Testcontainers
cd web && npm run lint && npm run typecheck && npm test && npm run build
```

Not yet automated: R1 (clean `docker compose up`) and R5 (OpenAPI diff) — R1 was verified manually in Phase 2 (see IMPLEMENTATION_PLAN.md §4); the GitLab pipeline has not run yet (no remote).

### 0.4 Added in BE3 (finance) — written 2026-10-18, **not yet executed**

Owner rule for BE3: compile only (`dotnet build`, 0 warnings); the suites below are written and compile but will be
run once in the final test stage. Results are therefore unknown until then.

| Id | Test (class) | Proves |
|---|---|---|
| F1 F2 F3 | `F1_F2_F3_two_payments_a_partial_refund_and_ten_percent_of_collected` (FinanceLedgerTests) | receivable 300 000.00 → 0.00, collected 500 000.00; net 400 000.00; commission lines +20 000.00, +30 000.00, −10 000.00 = 40 000.00, rate 0.1000 and the rule on each; balance 500000/500000/100000/0 |
| F4 | `F4_same_idempotency_key_sequentially…`, `F4_concurrent_duplicates_with_one_idempotency_key…` (Concurrency, 50×), `F4_the_same_key_used_by_another_user…` | 1 payment, 1 payment entry, 1 commission entry; replay returns the original; other body → 422; missing key → 400; per-clinic UNIQUE backstop |
| F5 F6 | `F5_F6_rounding_happens_once_per_line…` + unit `FinanceRulesTests` | 25 000.00; 3 × 0.02 = 0.06 (not 0.05) |
| F7 | `F7_the_rule_is_selected_by_the_payment_time…` | 8 000.00 with the 8 % rule snapshotted, posting date 2026-03-15; completion dated 2026-03-14 |
| F8 | `F8_the_reporting_day_is_the_branch_local_date` + unit | 18:30Z → 2026-03-31, 19:30Z → 2026-04-01 |
| F9 | `F9_an_april_refund_of_a_march_payment…` | March totals unchanged; April refunded = priorPeriodRefunded = 30 000.00 |
| F10 | `F10_a_failed_payment_is_stored_as_failed_with_no_ledger_lines` | status Failed, 0 entries, no allocation, paid 0.00 |
| F11 | `F11_a_mistyped_cash_payment_is_corrected…` | original entry untouched, reversal −30 000 (`reverses_entry_id`), new 300 000, commission +3 000/−3 000/+30 000, audit row, second fail → 409 |
| F12 | `F12_two_admins_create_the_same_monthly_settlement_concurrently_201_and_409` (Concurrency, 50×) | exactly one 201 + one `finance.settlement_exists`, 1 settlement, each line in one settlement |
| F13 | `F13_daily_summaries_of_march_sum_exactly_to_the_ledger_of_march` | Σ cache = `LedgerTotals` = raw SQL Σ per account, exact |
| F14 | `F14_a_clinic_without_a_rule…` | 10 paid visits → commission 0.00, `commissionRuleConfigured: false`, settlement amount 0.00 flagged |
| F15 | `F15_a_refund_larger_than_the_net_paid_amount…` | 422 `finance.refund_exceeds_paid`, row counts (payments, entries, lines, audit) unchanged |
| L1 | `FinanceSchemaTests.L1_…` + `DatabaseRoleTests` (extended) | UPDATE/DELETE/TRUNCATE on entries/lines → 42501 for the owner (trigger) and the app roles (REVOKE); rules cannot be deleted |
| L2 | `FinanceSchemaTests.L2_…` + `FinanceInvariants.AssertAsync` after every finance test | unbalanced / single-line / zero-line entries rejected at commit (23514); every entry balances, refunds ≤ payment, failed payments net 0, settlement items are commission lines of their month |
| L3 | `L3_rebuilding_the_summaries_equals_the_stored…` | second rebuild writes nothing; tampered row → 1 mismatch, overwritten, audit `finance.summary.mismatch`; monthly `reconciled` true → false → true |
| — | `Completed_service_new_patient_and_confirmed_appointment_bases…` | all four bases; NewPatient once per patient; walk-in → no confirm fee; price concession reverses CompletedService proportionally (−2 000.00) |
| — | `Prepayment_before_completion_is_patient_credit…` | outstanding −200 000 → −50 000 after completion; refund returns credit first, rest reduces revenue |
| — | `Period_lock_redirects_postings…`, `Settlement_lifecycle…` | redirect to next open month, locked month never restated, void Finalized → 409 `finance.period_locked`, void releases items, paid posts clearing, current month → 400 |
| — | `Concurrent_refunds_of_one_payment_never_exceed…` (50×), `Finalize_racing_a_posting_into_the_same_month…` (50×) | exactly 3 of 5 refunds; never an unsettled commission line in a finalized month |
| — | `FinanceApiTests` (HTTP, real sign-in) | authorization for **every** finance endpoint: Receptionist records (own branch; other branch 403) but cannot refund or view reports/settlements; Doctor and ClinicAdmin 403; FinanceManager all; Branches-scoped finance manager limited; other clinic 404; platform settle grant vs none; contract shapes (string money, enums, rate "0.0750"), validation, dashboard `collected` |
| — | `FinanceSchemaTests` | exclusion constraint 23P01 (adjacent/other basis allowed), rule "only end" trigger, settlement unique active month (voided allowed), item line UNIQUE, period CHECK, composite FK 23503, amount CHECK |
| — | `FinanceSeedAndWorkerTests` | finance seed idempotent, balanced, rule labelled `DEMO-`; Worker job writes the cache; next-run delay in Asia/Tashkent |
| — | `EndpointAuthorizationTests` (unchanged) | covers the 16 new endpoints automatically |

Helpers: `FinanceWorld` / `FinanceCalls` (Finance/FinanceKit.cs) on top of `BookingHost`, `BookingWorld`, `Race`.

### 0.5 Added in BE4 (notifications, Telegram bot) — written 2026-10-18, **not yet executed**

Owner rule: compile only (`dotnet build`, 0 warnings); these tests are written and compile and run in the final test
stage. Results are unknown until then. Integration tests use `NotificationKit` (Notifications/NotificationKit.cs) on top of
`BookingHost`: Development DI graph → **Fake Telegram client** (`FakeTelegramMailbox`: captured messages, `FailNext`,
`SendBehavior` failure hooks, queued updates) and Fake SMS; Telegram enabled with a fake token; jitter 0.

| Id | Test (class) | Proves |
|---|---|---|
| N1 | `N1_telegram_500_keeps_the_appointment_retries_with_backoff_and_fails_after_six_attempts` (OutboxDispatcherTests) | every attempt → Pending with next attempt ≈ +1 m / 5 m / 30 m / 2 h / 6 h, not due before; 6th → Failed; 6 attempt rows (Telegram, Retry, 500, attempt 1…6); appointment still Pending, booking invariants hold |
| N2 | `N2_crash_after_send_does_not_resend…` | one message per dedup key; a row stuck Processing with an expired lease is reclaimed (attempts 2) and **not re-sent** to a recipient with a Sent attempt; without the attempt row (crash before it) exactly one duplicate — at-least-once, documented |
| — | `Lease_reclaim_takes_only_expired_processing_rows_and_concurrent_claims_are_disjoint` | live lease not claimed, expired lease claimed (attempt + 1), two concurrent claimers (barrier) get disjoint sets covering all 39 due rows (SKIP LOCKED) |
| N3 | `N3_without_a_telegram_link_confirmed_falls_back_to_sms_and_other_types_are_skipped` | confirmed → SMS to the contact phone (uz text); requested → Skipped `notify.no_channel`; reminders Skipped (Telegram only); no exception, no Telegram call |
| — | `Sms_fallback_is_used_only_for_rejected_confirmed_cancelled_by_clinic_and_rescheduled` | the four OD-11 types → SMS; requested/expired → Skipped |
| — | `Walk_ins_get_sms_to_the_contact_phone_for_clinic_cancel_and_reschedule_only` | walk-in creation enqueues nothing; reschedule + clinic cancel → 2 intents (no phone/name in payloads), 2 SMS to the walk-in contact, recipient key `contact` |
| N4 | `N4_staff_messages_go_to_linked_triage_staff_of_the_branch_and_never_contain_the_patient_phone_or_name` | receptionist gets "New request #XXXXXXXX" (en) + console button and the patient-cancel notice; FinanceManager (no triage) and a receptionist scoped to another branch get nothing; texts and payloads contain no patient phone or name |
| N5 | `N5_no_reminder_after_cancellation_and_the_dispatcher_rechecks_stale_reminders` | Booking cancels reminders on clinic cancel; reminders forced back to Pending for a cancelled appointment and for one moved behind Booking's back → Cancelled `reminder.stale`, nothing sent |
| — | `A_due_reminder_for_a_confirmed_appointment_is_sent_by_telegram` | 24 h and 2 h reminders delivered (uz texts) |
| — | `Linked_patient_gets_telegram_in_their_language_with_a_mini_app_button_and_no_sms` | ru text with doctor and branch address, web_app button `…/tg/appointments/{id}`, no SMS |
| — | `Blocked_bot_deactivates_the_chat_link_and_falls_back_to_sms_where_allowed` | 403 → link inactive (`blocked`); confirmed delivered by SMS, requested Skipped; later messages skip Telegram |
| — | `Telegram_disabled_means_sms_for_od11_types_even_with_a_link`, `Sms_failure_is_retried_and_never_touches_the_appointment`, `Unknown_types_fail_permanently_without_a_send` | channel rules, SMS retry (`sms.failed`), permanent failure |
| — | TelegramBotTests: `/start` **valid token** (link active, language ru, identity created, reply + Mini App button), **reused** token and **expired** / unknown token → `LinkInvalid`, other account's Telegram id → `LinkConflict` without merge; plain `/start` links a known Telegram identity, otherwise welcome; group/supergroup/bot/non-text ignored; `/stop` → inactive (`stopped`, audited), `/start` re-activates; `/appointments` lists only own upcoming Pending/Confirmed; per-chat rate limit; poller handles queued updates once and persists the offset; a blocked reply is not an error | bot behaviour |
| — | NotificationApiTests: admin notifications **authz** (anonymous 401, patient 403, other platform permission 403 for list and retry); list without payload/PII, fields, status filter, bad status 400; retry Failed → Pending (204, audited), again → 409, unknown → 404; **webhook** wrong path 404, missing/wrong secret 401, right secret 200 + bot reply, bad JSON 200, setWebhook registered on startup; webhook 404 in Polling mode; **dev Telegram endpoints** (simulate + list in Development, **404 in Production**); config `botId`; Production refuses Fake Telegram and an incomplete webhook setup | HTTP contracts |
| — | RetentionAndWorkerTests: `Retention_deletes_only_eligible_rows_and_never_touches_audit` (exact counts per table, Pending/Processing/Failed-40-days kept, attempts cascade, audit unchanged); `Worker_dispatcher_service_delivers_due_messages_on_its_timer` | retention scope; hosted service |
| — | Unit `NotificationRulesTests` | backoff 1 m/5 m/30 m/2 h/6 h, jitter bounds, retry_after, final at 6, schedule parsing; a Telegram template for every type × language, SMS only for OD-11 types (≤ 160 chars uz/en), branch-time formatting, staff texts with request number and no phone; language mapping; command parsing; bot id / webhook path; public-https button rule; startup guards (Fake refused in Production, webhook secret/base URL) |
| — | Unit `TelegramBotApiClientTests` | request URL/body (inline keyboard web_app/url, no parse_mode), error classification (403, chat not found, 400, 5xx, 401, 409), 429 short retry once / long returned, timeout and network transient, token never echoed, getUpdates parsing, setWebhook secret |
| — | `EndpointAuthorizationTests` (unchanged) | covers the new admin/webhook/dev endpoints automatically |

## 1. Layers

| Layer | Tooling | DB |
|---|---|---|
| Domain / Application unit | xUnit | none |
| Integration | xUnit + WebApplicationFactory + Testcontainers PostgreSQL 18 | real, migrated per class, reset between tests |
| Concurrency | N parallel connections/clients released by a Barrier, ≥ 50 iterations | real |
| Migration | empty DB → all Up → Down 1 → Up | real |
| Frontend | Vitest + Testing Library + MSW | none |
| Architecture | reflection: layer refs, no float money, no entities in contracts | none |

Concurrency tests assert invariants on **DB state**, not only on HTTP codes.

## 2. Security / tenant isolation (review brief A)

| Id | Test | Expected |
|---|---|---|
| S1 | Clinic A staff GETs a clinic B appointment by id | 404 |
| S2 | Clinic A staff confirms/cancels/assigns on a clinic B appointment | 404, DB unchanged |
| S3 | Receptionist with B1 access acts on a B2 appointment (same clinic) | 403 |
| S4 | Receptionist lists with `?branchId=B2` | 403, or empty with no B2 rows |
| S5 | Patient P1 reads/cancels P2's appointment | 404 |
| S6 | Confirm with a doctor from another clinic or branch | 404/422, no write |
| S7 | Confirm with a doctor who doesn't provide the service | 422 |
| S8 | Doctor reads an appointment not assigned to them | 403/404 |
| S9 | Receptionist/Doctor calls finance reports | 403 |
| S10 | Clinic A FinanceManager substitutes a clinic B id | 404 |
| S11 | ClinicAdmin grants ClinicOwner | 403 |
| S12 | Removed/suspended staff reuses an existing cookie | 401 |
| S13 | PlatformAdmin reads patient data without / with the grant | 403 / 200 + audit row |
| S14 | Linking a phone owned by another user | no merge, error, audit |
| S15 | OTP brute force and rate limits | 429 / challenge invalidated |
| S16 | Captured logs of a full flow contain no phone/OTP/token/cookie | pass |
| S17 | Every endpoint has an authz test or an explicit AllowAnonymous (enumerated by reflection) | new uncovered endpoints fail the build |
| S18 | Raw insert of an appointment with another clinic's branch | 23503 FK violation |

## 3. Booking concurrency (review brief B)

| Id | Test | Expected |
|---|---|---|
| B1 | 1 doctor, 20 concurrent requests for [10:00, 10:30) | 1 Pending, 19 × 409, 1 active block |
| B2 | 30 min [10:00, 10:30) vs 60 min [10:15, 11:15), one doctor, concurrent | exactly 1 succeeds |
| B3 | Adjacent [10:00, 10:30) + [10:30, 11:00) | both succeed |
| B4 | 2 doctors, 3 concurrent overlapping requests | 2 succeed on different doctors |
| B5 | A provides {X, Y}, B provides {X}. X request then Y request | X held on B, Y on A (least-flexible-first). Documents heuristic limits |
| B6 | Two staff confirm the same request concurrently | 1 Confirmed, 1 × 409, 1 history row, 1 outbox message |
| B7 | Confirm vs sweeper at the deadline (controlled clock) | exactly one of Confirmed/Expired |
| B8 | Confirm after the deadline with the sweeper stopped | rejected. Reads show Expired |
| B9 | Patient cancel vs staff confirm | one wins |
| B10 | Time off for D vs confirm assigning D, overlapping | one commits |
| B11 | Injected failure after the block insert | full rollback, no orphan |
| B12 | Same Idempotency-Key twice, sequential and concurrent. Same key with a different body | one appointment + replay / 422 |
| B13 | Reject/expire/cancel release the block. The slot is rebookable | pass |
| B14 | An overdue Pending with the sweeper off doesn't block a new booking | sweep-on-write expires it |
| B15 | Outside hours / across shift end / during time off / inactive service / unapproved clinic | 422 |
| B16 | Local time near UTC midnight (UTC+5) | correct date, stored UTC |
| B17 | Forced 40P01 | ≤ 3 retries, then 409 |
| B18 | Invariant after every test: no overlapping active blocks per doctor. Every active block ↔ Pending/Confirmed or TimeOff | 0 violations |

## 4. Financial (review brief C)

F1–F15 in FINANCIAL_RULES.md §8, plus: L1 UPDATE/DELETE on ledger_lines as the app role fails.
L2 every journal entry sums to 0. L3 summary rebuild = stored summary.

## 5. Notifications

N1 Telegram 500/timeout: the appointment stays committed, retried with backoff, Failed after the max
attempts. N2 crash after send: the dedup key bounds duplicates (at-least-once, documented). N3 no
Telegram link: fallback channel, no exception. N4 staff payload contains no patient phone/name (OD-10).
N5 no reminder after cancellation.

## 6. Release / QA (review brief D)

R1 clean clone + `.env` from the example → `docker compose up` healthy. R2 migrations on an empty DB
plus the idempotent script in CI. R3 seed refuses Production. R4 `/health/live` and `/health/ready`.
R5 OpenAPI diffed in CI. R6 `dotnet build -warnaserror`, `dotnet test`, `npm run build`, `npm test`,
lint, `dotnet list package --vulnerable`, `npm audit`, secret scan. R7 backup → restore into a fresh
container, row counts + ledger checksum equal.

## 7. When the independent reviews run

The review briefs need code to review. Release/QA: end of Phase 2 and Phase 8. Security: end of
Phase 3 and after 4–5. Concurrency: end of Phase 4. Finance: end of Phase 5.
