#!/bin/bash
# Runs once, on first initialisation of an EMPTY data directory (postgres image behaviour).
# Creates the application database and least-privilege roles (docs/DATABASE.md §3):
#   oqtish_owner  - owns the schema; used only by the migrator
#   oqtish_app    - NOLOGIN group holding DML rights on objects created by oqtish_owner
#   oqtish_api    - LOGIN, member of oqtish_app (API)
#   oqtish_worker - LOGIN, member of oqtish_app (Worker)
set -euo pipefail

for var in OQTISH_DB_OWNER_PASSWORD OQTISH_DB_API_PASSWORD OQTISH_DB_WORKER_PASSWORD; do
  if [[ -z "${!var:-}" ]]; then
    echo "Missing required environment variable $var" >&2
    exit 1
  fi
done

psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres \
  -v owner_password="$OQTISH_DB_OWNER_PASSWORD" \
  -v api_password="$OQTISH_DB_API_PASSWORD" \
  -v worker_password="$OQTISH_DB_WORKER_PASSWORD" <<'EOSQL'
CREATE ROLE oqtish_owner LOGIN PASSWORD :'owner_password';
CREATE ROLE oqtish_app NOLOGIN;
CREATE ROLE oqtish_api LOGIN PASSWORD :'api_password' IN ROLE oqtish_app;
CREATE ROLE oqtish_worker LOGIN PASSWORD :'worker_password' IN ROLE oqtish_app;

CREATE DATABASE oqtish OWNER oqtish_owner;
REVOKE ALL ON DATABASE oqtish FROM PUBLIC;
GRANT CONNECT, TEMPORARY ON DATABASE oqtish TO oqtish_app;

\connect oqtish
ALTER SCHEMA public OWNER TO oqtish_owner;
REVOKE CREATE ON SCHEMA public FROM PUBLIC;
GRANT USAGE ON SCHEMA public TO oqtish_app;

-- Objects later created by migrations (as oqtish_owner) get DML rights for the app group only.
-- Append-only tables (ledger, audit, status history) revoke UPDATE/DELETE in their own migrations.
ALTER DEFAULT PRIVILEGES FOR ROLE oqtish_owner GRANT USAGE ON SCHEMAS TO oqtish_app;
ALTER DEFAULT PRIVILEGES FOR ROLE oqtish_owner GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO oqtish_app;
ALTER DEFAULT PRIVILEGES FOR ROLE oqtish_owner GRANT USAGE, SELECT ON SEQUENCES TO oqtish_app;
ALTER DEFAULT PRIVILEGES FOR ROLE oqtish_owner GRANT EXECUTE ON FUNCTIONS TO oqtish_app;
EOSQL
