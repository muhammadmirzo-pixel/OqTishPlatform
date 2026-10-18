#!/usr/bin/env bash
# Creates a compressed, checksummed logical backup (pg_dump custom format) of the OqTish database.
#
#   deploy/backup/backup.sh                 # bundled compose database (service "postgres")
#   BACKUP_SOURCE=host deploy/backup/backup.sh   # database from OQTISH_HOSTDB_CONNECTION in .env
#
# Options (env): BACKUP_DIR (default ./backups), BACKUP_RETENTION_DAYS (default 14, 0 = keep all).
# Never deletes anything except backup files older than the retention period in BACKUP_DIR.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT"
BACKUP_DIR="${BACKUP_DIR:-$ROOT/backups}"
RETENTION="${BACKUP_RETENTION_DAYS:-14}"
SOURCE="${BACKUP_SOURCE:-compose}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"

set -a; [ -f .env ] && . ./.env; set +a

case "$SOURCE" in
  compose)
    DB_NAME="oqtish"
    OUT="$BACKUP_DIR/${DB_NAME}_${STAMP}.dump"
    # Same server, same roles: keep ownership and privileges (incl. default privileges and the
    # REVOKEs that make ledger/audit tables append-only), so a restore is exact.
    docker compose exec -T postgres pg_dump -U postgres -d "$DB_NAME" -Fc > "$OUT.partial"
    ;;
  host)
    : "${OQTISH_HOSTDB_CONNECTION:?set OQTISH_HOSTDB_CONNECTION in .env}"
    # Translate the Npgsql connection string into libpq environment variables.
    get() { printf '%s' "$OQTISH_HOSTDB_CONNECTION" | tr ';' '\n' | grep -i "^$1=" | head -1 | cut -d= -f2-; }
    export PGHOST="$(get Host)" PGPORT="$(get Port)" PGDATABASE="$(get Database)" PGUSER="$(get Username)" PGPASSWORD="$(get Password)"
    PGPORT="${PGPORT:-5432}"
    DB_NAME="$PGDATABASE"
    OUT="$BACKUP_DIR/${DB_NAME}_${STAMP}.dump"
    # Host server: roles may differ, so ownership/privileges are not carried over.
    pg_dump -Fc --no-owner --no-privileges > "$OUT.partial"
    ;;
  *) echo "BACKUP_SOURCE must be compose or host" >&2; exit 2 ;;
esac

# Only a complete dump gets its final name, so a crash never leaves a truncated "valid-looking" file.
mv "$OUT.partial" "$OUT"
chmod 600 "$OUT"
( cd "$BACKUP_DIR" && sha256sum "$(basename "$OUT")" > "$(basename "$OUT").sha256" )

# Sanity check: the archive must be readable and list the EF migrations history table.
if ! pg_restore --list "$OUT" | grep -q '__ef_migrations_history'; then
  echo "Backup verification failed: archive is unreadable or incomplete: $OUT" >&2
  exit 1
fi

if [ "$RETENTION" -gt 0 ]; then
  find "$BACKUP_DIR" -maxdepth 1 -type f \( -name '*.dump' -o -name '*.dump.sha256' \) -mtime +"$RETENTION" -print -delete
fi

echo "Backup written: $OUT ($(du -h "$OUT" | cut -f1))"
