# Backup and Restore

## 1. What is backed up

The whole OqTish PostgreSQL database: schema, migrations history, all tenant data, the financial
ledger and the audit log. Application containers are stateless. The Data Protection key ring (used for
cookies and encrypted TOTP secrets) is stored **inside the database**, so a database backup also
contains it.

Backups contain personal data (patient names and phone numbers). Treat backup files like the
production database: restricted access, encrypted off-site copies, and the same retention rules.

## 2. Tools in this repository

| Script | Purpose |
|---|---|
| `deploy/backup/backup.sh` | `pg_dump` in custom format (compose mode keeps ownership + privileges; host mode uses `--no-owner --no-privileges` because roles may differ), written atomically (`.partial` → final name), mode 600, SHA-256 checksum file, archive readability check, retention cleanup (`BACKUP_RETENTION_DAYS`, default 14) |
| `deploy/backup/restore.sh <file>` | Verifies the checksum, restores into a **new** database `oqtish_restore_<timestamp>` (never overwrites), and compares per-table row counts with the source database |

Both scripts support the two database modes:

| Mode | Backup | Restore |
|---|---|---|
| Bundled compose database (service `postgres`) | `deploy/backup/backup.sh` | `deploy/backup/restore.sh <file>` |
| PostgreSQL on the host (`OQTISH_HOSTDB_CONNECTION` in `.env`) | `BACKUP_SOURCE=host deploy/backup/backup.sh` | `RESTORE_TARGET=host deploy/backup/restore.sh <file>` |

Files go to `./backups/` (git-ignored), or to `BACKUP_DIR`.

## 3. Verified drill (2026-10-18)

The following was run on the development machine:

| Step | Result |
|---|---|
| `deploy/backup/backup.sh` (compose DB) | dump written, checksum created, archive check passed |
| `deploy/backup/restore.sh <dump>` | checksum OK. New database created with `btree_gist`, `citext` and the migrations history. Row counts identical to the source |
| `BACKUP_SOURCE=host deploy/backup/backup.sh` (host `OqTish_db`) | dump written, archive check passed |
| `RESTORE_TARGET=host deploy/backup/restore.sh <dump>` | checksum OK. Row counts identical |
| Privilege drill (compose): table owned by `oqtish_owner` with UPDATE/DELETE revoked from the app, then backup → restore | restored table still owned by `oqtish_owner`; `oqtish_api` INSERT/SELECT allowed, UPDATE `permission denied`, CREATE `permission denied` |
| Clean-up | all `oqtish_restore_*` test databases and the drill schema dropped |

The drill was run while the schema contained only the initial migration. **Repeat it after every
release that adds tables** (§6). The scripts compare every table, so they automatically cover new
schemas.

## 4. Schedule (production)

| What | When | Keep |
|---|---|---|
| `backup.sh` logical dump | daily 02:30 Asia/Tashkent | 14 days locally |
| Off-site encrypted copy of the newest dump | daily, after the dump | 90 days (weekly copies for 1 year) |
| Restore drill (§6) | monthly, and before every risky migration | record results in the ops log |

Example systemd timer (as a user in the `docker` group, from the repository directory):

```ini
# /etc/systemd/system/oqtish-backup.service
[Service]
Type=oneshot
WorkingDirectory=/opt/oqtish
ExecStart=/opt/oqtish/deploy/backup/backup.sh
User=oqtish

# /etc/systemd/system/oqtish-backup.timer
[Timer]
OnCalendar=*-*-* 02:30:00 Asia/Tashkent
Persistent=true
[Install]
WantedBy=timers.target
```

`systemctl enable --now oqtish-backup.timer`

Off-site copy: encrypt before upload (for example `age -r <recipient> file.dump > file.dump.age`)
and store the copy in a different location **inside Uzbekistan** (data localisation, SECURITY.md §5).

**Recovery objectives with daily dumps:** RPO ≤ 24 h, RTO about 30 min for databases of a few GB.
If the business needs a smaller RPO, add continuous WAL archiving / point-in-time recovery (e.g.
pgBackRest). That is a recommended hardening step and is not implemented here.

## 5. Restoring production (incident procedure)

1. **Decide the restore point** and announce downtime. Take a fresh backup of the current (broken)
   state first: `deploy/backup/backup.sh`. Never skip this step.
2. Restore into a new database: `deploy/backup/restore.sh backups/<file>.dump`. Check the row-count
   report and spot-check key tables (`booking.appointments`, `finance.ledger_lines`).
3. Stop writers: `docker compose stop api worker web`.
4. Swap the databases (compose mode, as superuser):
   ```bash
   docker compose exec -T postgres psql -U postgres -c 'ALTER DATABASE oqtish RENAME TO oqtish_broken_<date>'
   docker compose exec -T postgres psql -U postgres -c 'ALTER DATABASE "oqtish_restore_<ts>" RENAME TO oqtish'
   docker compose exec -T postgres psql -U postgres -d oqtish -c 'GRANT CONNECT, TEMPORARY ON DATABASE oqtish TO oqtish_app'
   ```
   In compose mode the dump carries ownership, grants, default privileges and the append-only REVOKEs,
   so the restored database has exactly the original permissions (verified in the drill, §3). Then run
   the migrator (`docker compose run --rm migrate`) so any newer migrations are applied.
5. Start: `docker compose up -d --wait`. Check `/health/ready` and sign in as staff.
6. Keep `oqtish_broken_<date>` until the incident review is closed, then drop it.

## 6. Monthly restore drill

```bash
deploy/backup/backup.sh
deploy/backup/restore.sh "$(ls -t backups/*.dump | head -1)"
# inspect the row-count report, then:
docker compose exec -T postgres psql -U postgres -c 'DROP DATABASE "oqtish_restore_<ts>"'
```

Record the date, dump size, restore duration and the result.
