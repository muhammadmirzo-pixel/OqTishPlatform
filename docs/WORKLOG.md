# Work Log and Execution Plan

A living record of what has been done and what comes next. If work is interrupted (session limit,
crash), resume from the first stage that is not ✅. The "How to resume" section explains how.

Owner instruction (2026-10-18): finish the whole platform autonomously (backend, UI, Telegram bot and
Mini App, docs, local testing guides). Resolve open questions yourself (recorded in DECISIONS.md §4).
Resume automatically after any interruption without waiting for a new command.

## Stages

| # | Stage | Owner | Status |
|---|---|---|---|
| 0 | Phase 1–2: design, scaffold, containers, CI, baseline commit `f5ccb6f` | lead | ✅ |
| 0b | Decisions (DECISIONS §4), API contract (API.md), CONVENTIONS.md, SECURITY §8, SRS.md | lead | ✅ |
| 0c | Backup/restore scripts + tested drill (BACKUP_AND_RESTORE.md) | lead | ✅ |
| FE | Complete frontend: patient site, clinic console, admin, Telegram Mini App (patient + staff), i18n uz/ru/en, nginx CSP for /tg; docs/FRONTEND.md | frontend agent | ✅ committed 6d9000d (79 tests; contract gaps listed in FRONTEND.md §9 for INT) |
| BE1a | Common infra (Result/ProblemDetails, CSRF header, Money, tx runner, row locks, audit, outbox write side, idempotency), identity/auth (OTP, password+TOTP, sessions, phone link, Telegram initData/widget, telegram link tokens), SMS Fake + Eskiz, tenancy+staff schema, access service + permissions, seed mechanism; docs/SERVICES.md started | backend agent | ✅ committed f09c450 (166 tests) |
| BE1b | Endpoints: clinics (register/profile), branches + hours, services + branch prices, staff + invitations (by phone) + `/me/invitations`, doctors + links + working hours, public catalogue (search/facets/clinic detail without availability), admin clinics/users verification + suspension, audit endpoints, **production bootstrap CLI `create-platform-admin`**, seed contributors (demo clinics/branches/services/doctors/staff) | backend agent | ✅ committed 6855dff (194 tests) |
| BE2 | Booking: time blocks + exclusion constraint, availability, create Pending (provisional hold, idempotency, limits), confirm/reject/cancel/reschedule/complete/no-show, walk-in, time off, calendar, expiry sweeper (Worker), patient endpoints, reviews + abuse reports, concurrency tests B1–B18 | backend agent | ✅ committed 22cd1d3 (lead run 299/300 — 1 failure to investigate in TEST stage; FE needs i18n for `schedule.link_in_use`) |
| BE3 | Finance: completions, payments/fail/refund, allocations, double-entry ledger (append-only, idempotent), commission rules + snapshots, settlements, daily/monthly reports, reconciliation job, tests F1–F15, L1–L3 | backend agent | ✅ committed a0de96c (build OK; tests written, run in TEST) |
| BE4 | Notifications: outbox dispatcher (retries/backoff/idempotent), Telegram client (Fake + real HTTP), bot polling/webhook, `/start <token>` linking, Mini App button, message templates uz/ru/en (no PII rules), SMS fallback, reminders 24h/2h, retention job, admin notification endpoints, tests N1–N5 | backend agent | 🔄 running (build-only) |
| FE-INT-2 | Reconcile web/ with booking, finance, notifications/Telegram shapes (after BE3/BE4) | frontend agent | ⏳ |
| TEST | **Run all test suites once** (dotnet test + web tests), investigate/fix every failure (incl. the BE2 one) | lead + agents | ⏳ after all coding |
| INT | Merge, regenerate migrations if needed, full build/tests, FE↔API contract reconciliation, docker images, compose up (bundled + host DB modes), end-to-end smoke | lead (+ agent) | ⏳ |
| REV | ONE combined review (authz/tenant isolation, concurrency, finance, release) | 1 review agent | ⏳ |
| FIX | Fix all confirmed findings, re-run everything | agents + lead | ⏳ |
| DOC | **All documentation in Uzbek (Latin) as far as possible** (owner request 2026-10-18): translate existing English docs; identifiers/commands/code stay English. Final docs: DEPLOYMENT.md, SERVICES.md complete, API.md synced, TESTING.md, Uzbek local-testing guide (docs/ISHGA_TUSHIRISH.md + full test scenarios), final report | lead | ⏳ |

## Constraints that must survive interruptions

- **Owner rule (2026-10-18 evening): do NOT run test suites after each stage. Agents compile only (`dotnet build`, `npm run typecheck`/`build`) and write tests without executing them. All tests run once in the TEST stage, after all coding is done.** Keep the machine light: one .NET agent at a time.

- Machine RAM is tight (~1 GB free). Run at most two agents at once (one .NET + one frontend), and use
  `MSBUILDDISABLENODEREUSE=1`.
- Agents don't commit. The lead commits at stage boundaries and checks for secrets before each commit:
  `git grep -nE 'Password=(root|[A-Za-z0-9]{6,})' -- ':!*.md' ':!.env.example'`.
- Documentation language: Uzbek (Latin) as far as possible; code and code comments in English.
- Never put the host DB password in tracked files. It lives in `.env` (`OQTISH_HOSTDB_CONNECTION`) and in
  user-secrets.
- Don't touch the user's host DB `OqTish_db` until INT (then: migrate + seed via the documented commands).
- The compose stack is stopped during development (to save memory). Restart it at INT.

## How to resume after an interruption

1. `cd /home/muhammadmirzo/Projects/OqTishProgram && git status --short | head -50 && git log --oneline | head`.
2. Read this table and find the first stage that isn't ✅.
3. A stage marked 🔄 whose agent no longer runs (the session ended) has partial work in the tree:
   - run `dotnet build` / `dotnet test` (and the `web` checks) to see the state;
   - launch a new agent for that stage with "continue from the current working tree; first assess what
     exists, then finish the remaining scope". The briefs are the stage descriptions above plus
     CONVENTIONS.md / API.md.
4. Update this file when a stage starts or finishes, and commit at stage boundaries.

## Log

- 2026-10-18 ~04:45 (local): baseline committed. Decisions, API contract, conventions, SRS, backup
  scripts written. FE and BE1a agents launched.
- 2026-10-18: owner asked for documentation in Uzbek as far as possible; running agents informed; DOC stage will translate existing English docs.
- 2026-10-18 ~05:35: session limit hit; both agents stopped mid-work (state saved). 07:34: limit reset, FE and BE1a agents resumed from where they stopped.
- 2026-10-18 ~08:10: BE1a finished (166/166 tests verified by lead), committed f09c450. BE1b launched.
- 2026-10-18 ~08:20: FE finished (79/79 verified by lead), committed 6d9000d. BE1b told to match FE-assumed DTO shapes (web/src/api/types.ts).
- 2026-10-18 ~09:00: session limit hit again during BE1b (state saved). 12:34: limit reset, BE1b resumed.
- 2026-10-18 ~12:55: BE1b finished (194/194 verified), committed 6855dff. BE2 (booking) launched.
- 2026-10-18 ~13:20: FE-INT-1 agent launched in parallel (reconcile web/ with implemented BE1a/BE1b shapes; web/ + docs/FRONTEND.md only).
- 2026-10-18 ~13:45: FE-INT-1 finished (93/93 verified), committed 4259eab. Remaining FE contract gaps: FRONTEND.md §9.2 (booking/finance/telegram shapes → reconcile after BE2–BE4).
- 2026-10-18 ~14:30: session limit hit during BE2 (implementation + tests done, docs pending). 17:34: limit reset, BE2 resumed.
- 2026-10-18 ~18:20: owner resumed work: docs in Uzbek; tests only at the very end; wants a full local-run guide. BE2 committed 22cd1d3. Next: BE3 (finance), then BE4 (notifications/Telegram), sequentially.
- 2026-10-18 ~20:00: session limit hit during BE3 (writing HTTP authz tests). 22:34: limit reset, BE3 resumed.
- 2026-10-18 ~22:50: BE3 finished (build 0 warnings), committed a0de96c. BE4 (notifications + Telegram bot) launched.
- 2026-10-18 ~23:00: owner: token use too high. Heartbeat cron deleted; sonnet for mechanical agents; 1 combined review; translate only key docs. See memory token-economy.
