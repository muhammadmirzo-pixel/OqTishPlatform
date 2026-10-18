#!/usr/bin/env bash
# Restores a backup into a NEW database and verifies it. It never overwrites an existing database.
#
#   deploy/backup/restore.sh backups/oqtish_20261018T000000Z.dump            # into compose postgres
#   RESTORE_TARGET=host deploy/backup/restore.sh <file>                      # into the host server
#
# Result: database "oqtish_restore_<timestamp>" owned by the migrations role, plus a row-count
# comparison against the source database (when the source is reachable). Switching the application
# to the restored database is a deliberate manual step (docs/BACKUP_AND_RESTORE.md).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
FILE="${1:?usage: restore.sh <backup.dump>}"
TARGET="${RESTORE_TARGET:-compose}"
NEW_DB="oqtish_restore_$(date -u +%Y%m%dT%H%M%SZ | tr 'A-Z' 'a-z')"

[ -f "$FILE" ] || { echo "No such file: $FILE" >&2; exit 2; }
if [ -f "$FILE.sha256" ]; then
  ( cd "$(dirname "$FILE")" && sha256sum -c "$(basename "$FILE").sha256" )
else
  echo "Warning: no checksum file next to $FILE; integrity not verified." >&2
fi

set -a; [ -f .env ] && . ./.env; set +a

case "$TARGET" in
  compose)
    OWNER="oqtish_owner"
    psqlc() { docker compose exec -T postgres psql -U postgres -v ON_ERROR_STOP=1 -X "$@"; }
    psqlc -d postgres -c "CREATE DATABASE \"$NEW_DB\" OWNER $OWNER"
    psqlc -d postgres -c "REVOKE ALL ON DATABASE \"$NEW_DB\" FROM PUBLIC" -c "GRANT CONNECT, TEMPORARY ON DATABASE \"$NEW_DB\" TO oqtish_app"
    # Ownership, grants, default privileges and append-only REVOKEs come from the dump itself.
    docker compose exec -T postgres pg_restore -U postgres -d "$NEW_DB" --exit-on-error < "$FILE"
    SOURCE_DB="oqtish"
    ;;
  host)
    : "${OQTISH_HOSTDB_CONNECTION:?set OQTISH_HOSTDB_CONNECTION in .env}"
    get() { printf '%s' "$OQTISH_HOSTDB_CONNECTION" | tr ';' '\n' | grep -i "^$1=" | head -1 | cut -d= -f2-; }
    export PGHOST="$(get Host)" PGPORT="$(get Port)" PGUSER="$(get Username)" PGPASSWORD="$(get Password)"
    PGPORT="${PGPORT:-5432}"
    OWNER="$PGUSER"
    SOURCE_DB="$(get Database)"
    psqlc() { psql -v ON_ERROR_STOP=1 -X "$@"; }
    psqlc -d postgres -c "CREATE DATABASE \"$NEW_DB\" OWNER \"$OWNER\""
    pg_restore -d "$NEW_DB" --no-owner --exit-on-error "$FILE"
    ;;
  *) echo "RESTORE_TARGET must be compose or host" >&2; exit 2 ;;
esac

COUNT_SQL="SELECT n.nspname || '.' || c.relname, (xpath('/row/c/text()', query_to_xml(format('select count(*) as c from %I.%I', n.nspname, c.relname), false, true, '')))[1]::text::bigint
FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema') ORDER BY 1"

psqlc -d "$NEW_DB" -At -F ' ' -c "$COUNT_SQL" > "/tmp/oqtish_restore_counts_new.$$"
if psqlc -d "$SOURCE_DB" -At -F ' ' -c "$COUNT_SQL" > "/tmp/oqtish_restore_counts_src.$$" 2>/dev/null; then
  if diff -u "/tmp/oqtish_restore_counts_src.$$" "/tmp/oqtish_restore_counts_new.$$"; then
    echo "Verification: all $(wc -l < "/tmp/oqtish_restore_counts_new.$$") tables have identical row counts to $SOURCE_DB (expected only if nothing changed since the backup)."
  else
    echo "Note: row counts differ from the live source database (normal if data changed after the backup)."
  fi
else
  echo "Source database not reachable; restored table counts:"; cat "/tmp/oqtish_restore_counts_new.$$"
fi
rm -f /tmp/oqtish_restore_counts_*."$$"

echo "Restored into database: $NEW_DB"
echo "Drop it when no longer needed:  DROP DATABASE \"$NEW_DB\";"
