# OqTish — Dental Clinic Appointment Platform

A multi-tenant platform that connects independent dental clinics, their branches, staff and patients.
Patients request appointments, clinics assign doctors and confirm, and the platform records payments
and commission in an append-only ledger.

**Status: Phase 2 (scaffold).** The solution, database setup, containers, CI and test harness are in
place. No business features exist yet (they start in Phase 3).

## Repository layout

```
src/OqTish.Domain          domain model (no dependencies)
src/OqTish.Application     use cases, authorization rules, DTOs
src/OqTish.Infrastructure  EF Core + PostgreSQL, migrations, external adapters
src/OqTish.Api             ASP.NET Core API (health checks, OpenAPI, ProblemDetails, rate limiting)
src/OqTish.Worker          background jobs (currently: database readiness gate only)
web/                       React + TypeScript (Vite)
tests/                     architecture tests, PostgreSQL integration tests (Testcontainers)
deploy/                    Dockerfiles, nginx config, PostgreSQL init (roles)
docs/                      design documents (start with docs/IMPLEMENTATION_PLAN.md)
```

## Prerequisites

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | 10.0.401 (pinned in `global.json`) | |
| Node.js | 24 LTS | `web/package.json` `engines` |
| Docker Engine + Compose | 29 / v5 | required for containers **and** integration tests |

If Docker Desktop is installed but not running, point the CLI at the system engine:
`export DOCKER_CONTEXT=default` (for Compose) and `export DOCKER_HOST=unix:///var/run/docker.sock`
(for Testcontainers in `dotnet test`).

## Run the full stack (Docker Compose)

```bash
cp .env.example .env            # then replace every __REPLACE_ME__ with a strong random value
docker compose -f compose.yaml -f compose.dev.yaml up --build -d
docker compose ps               # api should become "healthy"; migrate exits 0
open http://localhost:8080      # SPA; shows "Server holati: Ishlayapti" when the API is ready
open http://localhost:8080/swagger/   # Swagger UI (Development overlay only)
```

Startup order: `postgres` (healthy) → `migrate` (applies EF Core migrations as `oqtish_owner`, then
exits) → `api` + `worker` (connect as least-privilege roles) → `web` (nginx: SPA + `/api` proxy).

`compose.yaml` alone is the production-like baseline (no DB port, `Production` environment, Swagger off).

**Using a PostgreSQL installed on this machine instead** (local development only): set
`OQTISH_HOSTDB_CONNECTION="Host=localhost;Port=5432;Database=OqTish_db;Username=...;Password=..."` in
`.env` (keep the double quotes), then run
`docker compose -f compose.yaml -f compose.hostdb.yaml up -d --build --wait`. The bundled `postgres`
service doesn't start in this mode, and the services join the host network, bound to `127.0.0.1`
(API :5080, web :8080). Running without Docker, `dotnet run` reads the same connection string from
user-secrets (`dotnet user-secrets set "ConnectionStrings:Main" "..." --project src/OqTish.Api`, and the
same for `src/OqTish.Worker`). Details: [docs/ISHGA_TUSHIRISH.md](docs/ISHGA_TUSHIRISH.md) §2A.

Stop: `docker compose down`. **`docker compose down -v` deletes the database volume.** Use it only
for a disposable local database.

## Run locally without containers

```bash
dotnet tool restore
# Needs a PostgreSQL you control, e.g. the compose one: docker compose -f compose.yaml -f compose.dev.yaml up -d postgres migrate
export ConnectionStrings__Main="Host=localhost;Port=55432;Database=oqtish;Username=oqtish_api;Password=<OQTISH_DB_API_PASSWORD from .env>"
dotnet run --project src/OqTish.Api           # http://localhost:5080
cd web && npm ci && npm run dev               # http://localhost:5173, proxies /api and /health to :5080
```

## Database migrations

```bash
# Add a migration after changing the model
dotnet tool run dotnet-ef migrations add <Name> \
  --project src/OqTish.Infrastructure --startup-project src/OqTish.Infrastructure \
  --output-dir Persistence/Migrations

# Review the SQL (idempotent script, as CI produces)
dotnet tool run dotnet-ef migrations script --idempotent \
  --project src/OqTish.Infrastructure --startup-project src/OqTish.Infrastructure

# Apply to a database you control (never an unknown or production DB without approval + backup)
OQTISH_MIGRATIONS_CONNECTION="Host=...;Database=oqtish;Username=oqtish_owner;Password=..." \
  dotnet tool run dotnet-ef database update \
  --project src/OqTish.Infrastructure --startup-project src/OqTish.Infrastructure
```

The API never migrates on startup. `/health/ready` reports unhealthy while migrations are pending.

## Tests and checks

```bash
dotnet build                     # warnings are errors
dotnet test                      # architecture + integration tests (starts a postgres:18-alpine container)
cd web
npm run lint && npm run typecheck && npm test && npm run build
npm audit --omit=dev
```

See [docs/TESTING.md](docs/TESTING.md) for the full required test catalogue and what exists today.

## Documents

| Document | Contents |
|---|---|
| [docs/ISHGA_TUSHIRISH.md](docs/ISHGA_TUSHIRISH.md) | ishga tushirish qo'llanmasi (o'zbek tilida) |
| [docs/IMPLEMENTATION_PLAN.md](docs/IMPLEMENTATION_PLAN.md) | phases, exit criteria, risk register, phase reports |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | system diagram, modules, booking/confirm sequences |
| [docs/DATABASE.md](docs/DATABASE.md) | ER diagram, constraints, DB roles, RLS, migration policy |
| [docs/SECURITY.md](docs/SECURITY.md) | authn/authz model, permission matrix, privacy |
| [docs/FINANCIAL_RULES.md](docs/FINANCIAL_RULES.md) | ledger, commission, numeric test cases, open accounting rules |
| [docs/TESTING.md](docs/TESTING.md) | test strategy and catalogue |
| [docs/DECISIONS.md](docs/DECISIONS.md) | ADRs, package versions, assumptions, open business decisions |
