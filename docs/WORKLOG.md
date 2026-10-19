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
| BE4 | Notifications: outbox dispatcher (retries/backoff/idempotent), Telegram client (Fake + real HTTP), bot polling/webhook, `/start <token>` linking, Mini App button, message templates uz/ru/en (no PII rules), SMS fallback, reminders 24h/2h, retention job, admin notification endpoints, tests N1–N5 | backend agent | ✅ committed 8396d76 (build OK) |
| FE-INT-2 | Reconcile web/ with booking, finance, notifications/Telegram shapes | frontend agent (sonnet) | ✅ a8e1536 (93/93) |
| TEST | **Run all test suites once** (dotnet test + web tests), investigate/fix every failure (incl. the BE2 one) | lead | ✅ backend 426/428 first run → 2 fixed (template, test race); BE2 flake did not recur |
| INT ✅ | (done 2026-10-18 ~23:50: host-DB compose rebuilt, Notifications migration applied to OqTish_db, seed OK, HTTP smoke: OTP sign-in → book → confirm → double-confirm 409 → payment/idempotent replay → refund rules → balance → daily report commission 4000.00 correct; authz 403/404 and CSRF 403 correct) Merge, regenerate migrations if needed, full build/tests, FE↔API contract reconciliation, docker images, compose up (bundled + host DB modes), end-to-end smoke | lead (+ agent) | ⏳ |
| REV | ONE combined review | 1 review agent | ✅ done: R1 High (client IP behind outer proxy), R2–R10 Medium/Low |
| FIX | Fix review findings in order R1 → R2 → R3 → R4 → R6 → R7 → R8 → R10 → R5; R9 (DP key encryption) = documented deployment step. Lead fixes directly; targeted tests only | lead | ✅ R1,R2,R3,R4,R6,R10 fixed; R7 false positive; R8 kept by design; R5 known risk; R9 deploy step (DECISIONS §6) |
| DOC ✅ | **All documentation in Uzbek (Latin) as far as possible** (owner request 2026-10-18): translate existing English docs; identifiers/commands/code stay English. Final docs: DEPLOYMENT.md, SERVICES.md complete, API.md synced, TESTING.md, Uzbek local-testing guide (docs/ISHGA_TUSHIRISH.md + full test scenarios), final report | lead | ⏳ |

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
- 2026-10-18 ~23:40: BE4 committed 8396d76. FE-INT-2 launched (sonnet).
- 2026-10-18 ~23:35: TEST stage: 428 backend tests, 426 passed on first run; 2 fixed by lead and re-verified. Frontend 93/93.
- 2026-10-18 ~23:50: INT done (see stage row). Launching ONE combined review agent.
- 2026-10-19: owner ok'd: fix review findings (most important first), then Uzbek docs + final report. Resume automatically after limits.
- 2026-10-19: FIX stage done (see DECISIONS §6); targeted tests green (268 + 16 + 8 + FE 12). Next: DOC (Uzbek key docs, DEPLOYMENT.md, final report).
- 2026-10-19: DOC done — README, DEPLOYMENT.md, YAKUNIY_HISOBOT.md (Uzbek), ISHGA_TUSHIRISH updated, smoke test 24/24 on latest code. All stages complete.

## UI/UX qayta dizayn (2026-10-19)

- Phase A (Discovery) tayyor: `docs/FRONTEND_PHASE_A.md`. Egasining tasdig‘i kutilmoqda (yo‘nalish, xarita, foto, `/` landing). Tasdiqdan keyin Phase B (dizayn tizimi) boshlanadi.
- 2026-10-19: Egasi referens dizaynni berdi → §0 "Moviy klinika" tasdiqlandi. Uzbek kirill (uz-Cyrl, avtomatik transliteratsiya) qo'shildi. Phase B (dizayn tizimi) tugadi: tokens/base/components/layouts CSS, Inter, Lucide, Toast/Drawer/Skeleton/Tooltip/CountdownRing/Monogram/Button, yangi sayt va konsol qobig'i, /__ui galereya (dev). Keyingi: Phase C (landing, katalog /clinics, klinika sahifasi, 3 qadamli booking, holat sahnasi; uz sana formatidagi "M10" xatosi).
- 2026-10-19: Phase C tugadi: landing (/), katalog (/clinics), klinika sahifasi (filial tanlagich), 3 bosqichli booking + xulosa paneli, holat sahnasi (Pending halqasi, polling 15 s), Qabullarim (chip filtr, sana bloki), hisob menyusi. Keyingi: Phase D (klinika paneli).
- 2026-10-19: Egasi ishga ketdi — ish to'xtatildi. Davom ettirish: docs/FRONTEND_RESUME.md (keyingi qadam: Phase D dashboard).
- 2026-10-19: Phase D (1/5): klinika dashboard'i — xush kelibsiz + filial sanasi, ikonkali metrikalar va ta'riflar (InfoTip), bugungi qabullar jadvali (mobil kartochka), tezkor amallar, 14 kunlik moliya grafigi (charged/collected, kutubxonasiz SVG, jadval ko'rinishi, dataviz validator bilan tekshirilgan ranglar).
- 2026-10-19: Phase D (2/5): so'rovlar qutisi — muddat bo'yicha kartochkalar, 10 daqiqadan kam qolganiga "Shoshilinch" belgisi, natija Toast'da (eskirgan so'rov ogohlantirishi sahifada), skeleton, J/K/Enter (faqat ro'yxat fokusda). PageHeader'ga ikonka.
- 2026-10-19: Phase D (3/5): kalendar — Kun | Hafta | Ro'yxat; shifokor ish vaqti foni, filial vaqti bo'yicha "hozir" chizig'i, ustma-ust bloklar yonma-yon, boshqa filialdagi bandlik, legenda; hafta = bitta shifokor, 7 ta kunlik so'rov (D6 endpoint yo'q).
- 2026-10-19: Phase D (4/5): konsol topbar'i — klinika/filial kontekst chip'lari (ikonka, almashganda bir martalik pulse), mobil'da bitta qator (til/mavzu menyu ichida), menyuda "Klinika profili" Sozlamalar guruhiga; J4 testi (filial almashganda eski raqam ko'rinmaydi).
- 2026-10-19: Phase D (5/5): qolgan klinika sahifalari — sarlavha ikonkalari, jadvallar mobil'da kartochka (data-label), ikonkali bo'sh holatlar, saqlash Toast'da; moliya atamalariga InfoTip ta'riflari (FinanceTerm), oylik hisobotda Taxminiy/Yakuniy (locked); hisob-kitob davri uz oy nomlari bilan; uz-Cyrl'da IANA zona id lotincha; mobil konsolda sidebar cho'zilishi tuzatildi. Phase D tugadi.
- 2026-10-19: Phase E (1/3): xavfli amal dialoglari (danger: qizil ramka, ogohlantirish, qizil tugma; qaytarib bo'lmaydiganlarga "Tushundim" belgisi), admin dashboard ikonkali metrikalar, klinikalar/foydalanuvchilar/hisob-kitoblar — ikonkalar, mobil jadval, Toast; rad etish/to'xtatish/bloklash/void — sabab + danger, finalize — acknowledge.
- 2026-10-19: Phase E (2/3): shikoyatlar va sharhlar moderatsiyasi (ikonkalar, Toast, yashirish — sabab + danger), komissiya (qoidani tugatish — danger + "Tushundim"), break-glass (ogohlantirish strip, mobil jadval).
- 2026-10-19: Phase E (3/3): audit va xabarnomalar navbati (holat ikonka+matn, qayta urinish Toast), admin sidebar'ida "Platforma" belgisi, mobil jadval kartochkasi setkaga o'tdi (yorliq chapda, qiymatlar o'ngda ustma-ust) — barcha table--stack'larga ta'sir. Phase E tugadi.
- 2026-10-19: Phase F (boshlandi): to'liq vitest 112/112; build: bemor/akkaunt sahifalari alohida 'patient' chunk, /__ui galereya production'dan chiqarildi; boshlang'ich JS 188 → ~181 kB gzip (maqsad 180; qolgani react-dom + react-router + i18n).
- 2026-10-19: Phase F (2/n): C12 — uz matnlarida apostrof normallashtirildi (270 qiymat: o'/g' → o‘/g‘, tutuq → ’); kalitlar, ru/en o'zgarmagan; vitest 112/112.
- 2026-10-19: Phase F (3/n): Playwright UI to'plami (web/e2e, mock API — faqat QA): 9 ekran × 360/768/1280/qorong'i/reduced-motion — axe WCAG 2.2 AA (serious/critical yo'q) + gorizontal skrol yo'q, 90/90. Topilgan a11y xatolari tuzatildi: mobil konsolda profil havolasi nomsiz, tab bar va qorong'i rejimdagi kontrastlar (faol menyu, btn--subtle, info badge, "hozir" yorlig'i).
- 2026-10-19: Phase F (4/n): taxminiy mobil o'lchov (preview build, 4× CPU, 1.6 Mbit/s): landing LCP 2.13 s, katalog 1.99 s, klinika sahifasi 2.24 s, CLS ≈ 0, JS 177 kB; dashboard LCP 2.81 s (so'rovlar zanjiri), CLS 0.063 → 0 (skeleton o'lchamlari). e2e/perf.spec.ts (E2E_PERF=1).
- 2026-10-19: Phase F (5/n): FRONTEND.md yangilandi (landing/katalog marshrutlari, dashboard, kalendar, dizayn tizimi, e2e), yakuniy hisobot docs/FRONTEND_YAKUNIY_HISOBOT.md, FRONTEND_RESUME.md. Qolgan: real backend bilan E2E (backend ishga tushmadi — Docker o'chiq, .env yo'q).
- 2026-10-19: Referens 12 qism bo'yicha solishtirish boshlandi. Qism 02 (mobil bosh ekran): yo'nalishlar va klinikalar gorizontal suriladi, qadamlar/afzalliklar ixcham qatorlar, kirgan foydalanuvchiga salom; mobil landing 6210 → 3900 px.
- 2026-10-19: Qism 03 (katalog): desktop'da ro'yxat qatorlari (rasm o'rni | ma'lumot | narx va tugmalar), mobil'da kartochka; ClinicCardView layout="row".
