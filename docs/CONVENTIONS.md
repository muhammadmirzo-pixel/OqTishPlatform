# Engineering Conventions

Binding rules for everyone who changes this repository (people and coding agents). The goal is that
modules built by different people fit together without guesswork.

## 1. Layers and where code goes

| Layer | Contains | May reference |
|---|---|---|
| `OqTish.Domain` | Entities, value objects (`Money`), enums, state machines (`Appointment.Confirm(...)` etc.), domain errors. Pure C#. | nothing |
| `OqTish.Application` | Use-case handlers (one class per command/query), request/response DTOs, validators, permission catalogue + access rules, ports (`IOqTishDb`, `ISmsSender`, `ITelegramClient`, `IOutbox`, `IAuditLog`, `IRowLocks` …). | Domain, `Microsoft.EntityFrameworkCore` (LINQ + `DbSet` only) |
| `OqTish.Infrastructure` | `OqTishDbContext` (implements `IOqTishDb`), EF configurations, migrations, Npgsql specifics (row locks, error classification), SMS/Telegram adapters, password hashing, outbox writer. | Application, Domain, Npgsql, EF Core |
| `OqTish.Api` | Minimal API endpoint groups (`Endpoints/<Module>Endpoints.cs`), auth cookie setup, rate-limit policies, ProblemDetails mapping, OpenAPI. **No business logic.** | Application, Infrastructure |
| `OqTish.Worker` | Hosted services: outbox dispatcher, expiry sweeper, reminders, Telegram polling, summaries, retention. Logic lives in Application handlers. | Application, Infrastructure |

Application must not reference Npgsql, ASP.NET Core or Infrastructure. Domain references nothing.
`ArchitectureTests` enforce this.

Module folders inside each layer: `Identity`, `Tenancy`, `Staff`, `Catalog`, `Scheduling`, `Booking`,
`Finance`, `Notifications`, `Reviews`, `Audit`, `Admin`, `Public`, plus `Common`.

No MediatR, AutoMapper, FluentValidation or other frameworks. Handlers are plain classes registered in
DI, and mapping is explicit.

## 2. Handler shape and results

```csharp
public sealed class ConfirmAppointmentHandler(IOqTishDb db, IAccessService access, ...) 
{
    public async Task<Result<AppointmentDto>> HandleAsync(ConfirmAppointmentCommand cmd, CancellationToken ct) { ... }
}
```

`Result<T>` carries either a value or an `Error(Kind, Code, Message, FieldErrors?)`.

| Error kind | HTTP |
|---|---|
| `Validation` | 400 |
| `Unauthorized` | 401 |
| `Forbidden` | 403 |
| `NotFound` | 404 |
| `Conflict` | 409 |
| `RuleViolation` | 422 |
| `RateLimited` | 429 |

Every error has a stable machine-readable `code` (`booking.slot_unavailable`, `auth.otp_invalid` …).
The API returns RFC 7807 ProblemDetails with the extensions `code`, `traceId` and optionally `errors`
(field → messages). The frontend localises errors by `code`. Every error code is listed in
`docs/API.md` §9.

Exceptions are for bugs and infrastructure failures only, never for business outcomes.

## 3. Authorization pattern (critical)

1. Endpoint → handler. The handler **loads the target resource including its `ClinicId` / `BranchId`**
   from the database, using the route id only as a lookup key.
2. `IAccessService.GetScopeAsync(ct)` gives the caller's effective scope. It is computed from DB state
   once per request: active memberships, branch grants, platform grants, suspension flags.
3. `scope.Check(Permission.X, clinicId, branchId)` returns:
   - `NotFound` when the caller has no active membership in that clinic (no tenant enumeration);
   - `Forbidden` when the membership exists but the role or branch scope lacks the permission.
4. List queries filter in SQL by `scope.BranchIdsWith(Permission.X, clinicId)`. They never filter
   after loading.
5. Patients: `appointment.PatientUserId == currentUser.Id`, otherwise `NotFound`.
6. Platform admins get only their explicit `PlatformPermission` grants. Reading tenant patient data
   requires `platform.tenant-data.read` plus an `X-Access-Reason` header, and every such read is audited.
7. Permission constants live in `Application/Staff/Permissions.cs`. The role → permission map there is
   the single source of truth and is snapshot-tested.

## 4. Data and transactions

- IDs are `Guid.CreateVersion7()`. Timestamps are UTC `DateTimeOffset` / `timestamptz`. Business
  "local" values are `DateOnly` / `TimeOnly` interpreted in the branch's IANA `TimeZone`.
- Money: the `Money` value object (`decimal` + `Currency`). The column type is `numeric(19,2)`.
  Rounding happens only through `Money.Round()` (2 dp, AwayFromZero).
- Every tenant-owned table has `clinic_id`. Child → parent FKs are composite `(clinic_id, parent_id)`.
- Each module maps into its own PostgreSQL schema (`booking.appointments`, `finance.ledger_lines` …).
  Naming is snake_case.
- Write transactions that must be atomic use `ITransactionRunner.RunAsync(isolation: ReadCommitted,
  maxAttempts: 3, …)`. It retries **only** on `40001` / `40P01`, with jitter.
- Row locks go through `IRowLocks` (`LockBranchAsync`, `LockAppointmentAsync`, …), implemented in
  Infrastructure with `SELECT … FOR UPDATE`. Lock order is always **branch → appointment → others**.
- PostgreSQL errors are classified by `IDbErrorClassifier`: `23P01` exclusion → `Conflict`; `23505`
  unique → `Conflict`, or an idempotent replay where applicable; `23503` FK → `NotFound`/`Validation`.
- Deadlines (`expires_at`) are compared with **database time** (`now()`) inside the write statement.
- Audit rows (`IAuditLog`) and outbox rows (`IOutbox`) are written **in the same transaction** as the
  business change.
- Append-only tables (ledger, audit, status history) are protected by `REVOKE UPDATE, DELETE` plus a
  trigger in their migration.

## 5. API conventions

- Base path `/api`. JSON is camelCase, enums are strings, and money is
  `{ "amount": "1500000.00", "currency": "UZS" }` (string amount).
- Instants are ISO-8601 UTC (`2026-10-20T05:00:00Z`). Local dates are `YYYY-MM-DD`, local times `HH:mm`.
- Lists take `?page=1&pageSize=20` (max 100) and return `{ items, page, pageSize, totalCount }`.
- Every non-GET request must carry the header `X-OqTish-Client: web` (or `tg-miniapp`), otherwise
  403 `security.client_header_missing`. This is the CSRF defence (OD-23).
- Booking, payment and refund POSTs accept `Idempotency-Key` (≤ 64 chars). A replay returns the stored
  response. The same key with a different body returns 422 `common.idempotency_mismatch`.
- Every endpoint has an OpenAPI name, summary and response types. `docs/API.md` must match the code.

## 6. Security and privacy rules

- Never log phone numbers (use the masked form `+998*****4567` only when needed), names, OTP codes,
  tokens, cookies, passwords, Telegram `initData`, or bot tokens.
- Secrets come only from configuration (env, user-secrets, Docker secrets). Never put them in tracked
  files, including `appsettings*.json`.
- OTP codes, link tokens and session ids are stored **hashed** (SHA-256 for random tokens, Identity
  password hasher for passwords).
- Development-only features (`Fake` SMS, `/api/dev/*`) must refuse to start or answer 404 outside
  `Development`. A test proves it.

## 7. Testing rules

- Every endpoint gets at least one positive and one negative authorization test. The endpoint-coverage
  test fails when a new endpoint has neither an authorization test nor `[AllowAnonymous]` metadata.
- Integration tests use real PostgreSQL (Testcontainers, `postgres:18-alpine`) and the real sign-in
  flow (OTP through the Fake SMS mailbox). No auth bypass handlers.
- Concurrency tests release N parallel requests with a `Barrier`, run ≥ 50 iterations (in a loop over
  fresh fixtures), and assert invariants on database state.
- Frontend: Vitest + Testing Library. The API is mocked at the `fetch` boundary.

## 8. Working in this repo (agents and people)

```bash
export PATH=$HOME/.dotnet:$HOME/.local/opt/node/bin:$PATH
export DOCKER_HOST=unix:///var/run/docker.sock DOCKER_CONTEXT=default
export MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1   # the machine is low on RAM
dotnet build && dotnet test                 # backend
cd web && npm run lint && npm run typecheck && npm test && npm run build
```

- Run `dotnet build-server shutdown` after heavy builds (the machine has little free memory).
- Migrations: `dotnet tool run dotnet-ef migrations add <Name> --project src/OqTish.Infrastructure
  --startup-project src/OqTish.Infrastructure --output-dir Persistence/Migrations`.
- Documentation is written in **Uzbek (Latin script)** as far as possible; technical identifiers, commands, code and code comments stay in English.
- Coding agents do **not** commit, and do not touch areas owned by another running agent. The lead
  integrates and commits.
