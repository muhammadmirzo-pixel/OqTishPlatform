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

### 1.1 Pinned versions (Phase 2, 2026-10-18)

| Component | Version |
|---|---|
| .NET SDK / runtime | 10.0.401 / 10.0.12 |
| Microsoft.* packages (EF Core Design, Identity EF, OpenApi, HealthChecks EF, Hosting, Mvc.Testing) | 10.0.12 (matches installed runtime) |
| Npgsql.EntityFrameworkCore.PostgreSQL | 10.0.3 |
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
