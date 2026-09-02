#!/usr/bin/env bash
set -euo pipefail

BACKUP_DIR="${STATEFALSE_BACKUP_DIR:-/var/backups/statefalse}"
KEY_FILE="${STATEFALSE_BACKUP_KEY_FILE:-/etc/statefalse/backup.key}"
PGHOST="${STATEFALSE_PGHOST:-localhost}"
PGPORT="${STATEFALSE_PGPORT:-5432}"
TEST_DATABASE="${STATEFALSE_RESTORE_TEST_DATABASE:-statefalse_restore_test}"

case "$TEST_DATABASE" in
  statefalse_restore_test) ;;
  *)
    echo "ERROR: restore test database name is fixed to statefalse_restore_test" >&2
    exit 1
    ;;
esac

for command_name in gpg pg_restore psql createdb find mktemp rm date stat; do
  command -v "$command_name" >/dev/null 2>&1 || {
    echo "ERROR: $command_name is required" >&2
    exit 1
  }
done

[ -d "$BACKUP_DIR" ] || { echo "ERROR: backup directory not found: $BACKUP_DIR" >&2; exit 1; }
[ -r "$KEY_FILE" ] || { echo "ERROR: backup key is not readable" >&2; exit 1; }

backup_file="$(find "$BACKUP_DIR" -maxdepth 1 -type f -name 'statefalse-*.dump.gpg' -printf '%f\n' | sort | tail -n 1)"
[ -n "$backup_file" ] || { echo "ERROR: no encrypted backup found" >&2; exit 1; }
backup_path="$BACKUP_DIR/$backup_file"
[ -s "$backup_path" ] || { echo "ERROR: selected backup is empty: $backup_file" >&2; exit 1; }

# A daily timer must not silently validate an arbitrarily old backup.
backup_age="$(find "$backup_path" -maxdepth 0 -printf '%T@' | cut -d. -f1)"
now="$(date +%s)"
if [ "$((now - backup_age))" -gt 93600 ]; then
  echo "ERROR: latest backup is older than 26 hours: $backup_file" >&2
  exit 1
fi

temp_dump="$(mktemp --suffix=.statefalse-restore.dump)"
chmod 600 "$temp_dump"
drop_test_database() {
  psql --no-psqlrc --host="$PGHOST" --port="$PGPORT" --dbname=postgres \
    --command='DROP DATABASE IF EXISTS "statefalse_restore_test" WITH (FORCE);' \
    >/dev/null
}
cleanup() {
  rm -f "$temp_dump"
  drop_test_database >/dev/null 2>&1 || true
}
trap cleanup EXIT

printf '[%s] Testing restore of %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$backup_file"

gpg --batch --yes --pinentry-mode loopback \
  --passphrase-file "$KEY_FILE" \
  --decrypt --output "$temp_dump" "$backup_path"

pg_restore --list "$temp_dump" >/dev/null

drop_test_database
createdb --host="$PGHOST" --port="$PGPORT" --template=template0 "$TEST_DATABASE"
pg_restore --exit-on-error --no-owner --no-privileges \
  --host="$PGHOST" --port="$PGPORT" \
  --dbname="$TEST_DATABASE" "$temp_dump"

table_count="$(psql --no-psqlrc --tuples-only --no-align \
  --host="$PGHOST" --port="$PGPORT" --dbname="$TEST_DATABASE" \
  --command="SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';" \
  | tr -d '[:space:]')"
case "$table_count" in
  ''|*[!0-9]*) echo 'ERROR: invalid table count after restore' >&2; exit 1 ;;
esac
[ "$table_count" -gt 0 ] || { echo 'ERROR: restored database contains no public tables' >&2; exit 1; }

printf '[%s] Restore test passed: %s tables restored into isolated database\n' \
  "$(date -u +%Y-%m-%dT%H:%M:%SZ)" "$table_count"
