# Testing Strategy and Required Test Catalogue

Status: Phase 2 harness in place. The catalogue below (§2–§6) is the required target; §0 lists what exists today.

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

Commands (from the repository root):

```bash
export DOCKER_HOST=unix:///var/run/docker.sock   # only if Docker Desktop is installed but not running
dotnet test                                      # 13 tests; starts postgres:18-alpine via Testcontainers
cd web && npm run lint && npm run typecheck && npm test && npm run build
```

Not yet automated: R1 (clean `docker compose up`) and R5 (OpenAPI diff) — R1 was verified manually in Phase 2 (see IMPLEMENTATION_PLAN.md §4); the GitLab pipeline has not run yet (no remote).

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
