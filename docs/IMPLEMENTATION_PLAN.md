# Implementation Plan and Risk Register

## 1. Repository inspection (2026-10-18)

| Check | Result |
|---|---|
| Repository | Empty directory, not a git repository. **Later in the session the path became an empty regular file** (not caused by these tools), so nothing could be written there |
| .NET SDK | not installed |
| Node.js / npm | not installed |
| Docker / Compose | CLI 29.9.0 / Compose v5.5.1. **Daemon unreachable** (Docker Desktop not running) |
| psql | 18.6 |
| git | 2.53.0 |

Update at Phase 2 start: the directory was restored. .NET SDK 10.0.401 was found in `~/.dotnet` (not on
PATH); the system Docker Engine works through the `default` context (Docker Desktop itself is not
running); Node.js 24.21.0 LTS was installed to `~/.local/opt/node` (checksum verified).

## 2. Phases and exit criteria (each verified by commands actually run)

| Phase | Scope | Exit criteria |
|---|---|---|
| 1 | Plan, architecture, ER, permission matrix, financial rules, risks | Answers to the open decisions needed for Phases 3–4 |
| 2 | git init, solution/projects (nullable, warnings-as-errors, central package mgmt), Vite app, Compose (postgres, api, worker, web, proxy), .env.example, health checks, logging, initial migration, GitLab CI, test projects + Testcontainers smoke test | build/test/npm build/npm test pass. Compose healthy. Migration on an empty DB |
| 3 | Identity (OTP, Telegram, staff password + TOTP), sessions/revocation, linking, tenancy, memberships + permissions, catalog, audit, rate limits | S-tests + permission snapshot |
| 4 | Doctors, hours, time off, blocks + exclusion constraint, availability, booking + transitions, sweeper, idempotency | B1–B18 over ≥ 50 iterations + concurrency review |
| 5 | Completions, payments, refunds, commission, ledger, settlements, summaries, reconciliation | F1–F15, L1–L3 + finance review |
| 6 | Outbox dispatcher, Telegram + linking, SMS fallback, reminders | N1–N5 |
| 7 | All frontend workflows, admin, reports, i18n, responsive | Vitest, build, 360/768/1280 px check |
| 8 | RLS, hardening, vulnerability scans, backup/restore drill, deploy/rollback docs | R1–R7, full suite, security + release review |

Each phase ends with a report: changed files, the commands run with their real results, and open issues.
Work stops for approval before destructive changes or deployment. SRS, API, DEPLOYMENT and
BACKUP_AND_RESTORE docs are written when they first have real content. SRS comes right after the
Phase 1 answers.

## 3. Risk register

| # | Risk | L | I | Mitigation | Verified by |
|---|---|---|---|---|---|
| R1 | Double booking (check-then-insert) | H | Critical | Exclusion constraint + branch lock + sweep-on-write | B1–B4, B10, B18 |
| R2 | Wrong capacity when the patient doesn't pick a doctor | H | H | Provisional doctor hold (OD-01) | B4, B5 |
| R3 | Confirm/expire/cancel race → two states or orphan holds | M | H | Row lock + conditional UPDATE + DB-time deadline | B6–B9, B13 |
| R4 | Cross-tenant/branch IDOR | H | Critical | Load-then-authorize, pre-filtered lists, composite FKs, endpoint coverage test, RLS | S1–S18 |
| R5 | Account takeover (OTP brute force, unsafe linking) | M | H | Limits, hashed OTP, proof-based linking, staff TOTP | S12, S14, S15 |
| R6 | Commission on an invented basis | H if guessed | H | No default, zero without a rule, snapshots | F7, F14 |
| R7 | Double-counted payments from retries | M | H | Unique idempotency/source keys, single transaction | F4, B12 |
| R8 | Silent edits to financial history | M | H | Grants + trigger, reversal entries | L1, F11 |
| R9 | Time zone / reporting-day errors | M | M | UTC + branch IANA zone + local posting date | F8, B16 |
| R10 | Notification failure rolls back a booking | L | M | Transactional outbox | N1 |
| R11 | Patients never link Telegram | H | M | Status page + SMS fallback (OD-11) | N3 |
| R12 | Data localisation / registration non-compliance | M | Critical | Local hosting, legal review (OD-12) | Legal sign-off |
| R13 | PII in logs/Telegram | M | H | Redaction + minimal payloads | S16, N4 |
| R14 | Clinics miss the 30-min window | H | M (business) | Alerts, lead-time rule (OD-03) | Metrics |
| R15 | No tested restore | L | Critical | pg_dump + PITR, restore drill | R7 |
| R16 | Scope too large for one pass | H | H | Strict phase gates. Defer distance/availability search | Phase reports |
| R17 | Toolchain missing, project dir broken | Present | Blocks Phase 2 | Fix before Phase 2 | §1 |

## 4. Phase 2 report (2026-10-18)

**Delivered**
- Solution `OqTish.slnx`: Domain, Application, Infrastructure, Api, Worker + ArchitectureTests, IntegrationTests. Nullable, warnings-as-errors, analyzers, central package management, NuGet audit, lock files.
- Infrastructure: `OqTishDbContext` (snake_case, extensions btree_gist + citext), design-time factory without embedded credentials, initial migration `InitialExtensions`.
- Api: JSON structured logging, ProblemDetails, OpenAPI + Swagger UI (Development only), `/health/live`, `/health/ready` (DB + pending migrations), forwarded headers trusted only from the backend subnet, baseline rate limiter.
- Worker: host with a database readiness gate (waits for a reachable, fully migrated DB).
- Web: Vite + React 19 + TypeScript (strict), oxlint, Vitest + Testing Library, typed API client with explicit error kinds, runtime `config.json`.
- Containers: multi-target .NET Dockerfile (api / worker / migrator EF bundle), unprivileged nginx web image (SPA + reverse proxy + security headers), PostgreSQL init script with least-privilege roles, `compose.yaml` (production-like) + `compose.dev.yaml`, `.env.example`.
- CI: `.gitlab-ci.yml` (build + migration script artefact, Testcontainers tests via dind, web lint/typecheck/test/build, dependency audits, secret detection, image build).

**Commands actually run and outcomes**

| Command | Outcome |
|---|---|
| `dotnet build` | 0 warnings, 0 errors |
| `dotnet test` | 13/13 passed (4 architecture, 9 integration on real PostgreSQL 18) |
| `npm run lint` / `npm test` / `npm run build` (web) | clean / 18/18 passed / built (220 kB JS, 69 kB gzip) |
| `npm audit` | 0 vulnerabilities |
| `docker compose -f compose.yaml -f compose.dev.yaml up -d --build --wait` | postgres healthy → migrate exit 0 ("Applying migration InitialExtensions") → api healthy, worker running, web healthy |
| curl through nginx :8080 | `/` 200, deep link 200 (SPA fallback), `/config.json` 200, `/health/ready` 200, `/openapi/v1.json` 200 (dev), `/api/nope` 404 `application/problem+json` with traceId only; 5/5 security headers on every route |
| `psql` as `oqtish_api`: `CREATE TABLE public.x` | `permission denied for schema public` |

**Problems found and fixed during the phase**
- .NET 10 `dotnet test` requires opting in to Microsoft.Testing.Platform → `global.json` `test.runner` (ADR-012); VSTest adapter packages removed.
- Migrator crashed (needed ASP.NET runtime) → removed an unnecessary `Microsoft.AspNetCore.App` framework reference from Infrastructure.
- Stale NuGet HTTP cache blocked restore → `dotnet restore --no-http-cache --force-evaluate` (one-time, local).
- nginx could not write `config.json` into the web root → file is written to `/tmp/oqtish` and served via `alias`; web root stays read-only.
- nginx `add_header` inheritance dropped headers in some locations → shared `security-headers.conf` snippet.
- Npgsql libgssapi probe noise → GSS encryption disabled by default.

**Not verified / open**
- The GitLab pipeline has never run (no remote configured; the `dotnet-test` job needs a privileged runner for docker:dind).
- The SPA was checked over HTTP only, not in a real browser; Swagger UI under the strict CSP was not checked in a browser (Development only).
- Nothing is committed yet; the repository has no commits.
- TLS, backups and production deployment are Phase 8 scope.
