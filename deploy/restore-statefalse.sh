#!/usr/bin/env bash
set -euo pipefail

SOURCE="${1:?Usage: restore-statefalse.sh BACKUP.dump DATABASE [--force]}"
DATABASE="${2:?Usage: restore-statefalse.sh BACKUP.dump DATABASE [--force]}"
FORCE="${3:-}"

PGUSER="${STATEFALSE_PGUSER:-statefalse}"
PGHOST="${STATEFALSE_PGHOST:-localhost}"
KEY_FILE="${STATEFALSE_BACKUP_KEY_FILE:-/etc/statefalse/backup.key}"

if [ ! -f "$SOURCE" ]; then
  echo "ERROR: backup not found: $SOURCE" >&2
  exit 1
fi
if [[ "$SOURCE" != *.dump.gpg ]] || [ ! -r "$KEY_FILE" ] || ! command -v gpg >/dev/null 2>&1; then
  echo "ERROR: encrypted backup and readable backup key are required" >&2
  exit 1
fi
if [ "$FORCE" != "--force" ]; then
  echo "ERROR: restore is destructive; pass --force after explicit approval" >&2
  exit 1
fi

echo "WARNING: This will drop and recreate the database '$DATABASE'." >&2
echo "Press Ctrl+C within 5 seconds to abort." >&2
sleep 5

temporary_dump="$(mktemp)"
trap 'rm -f "$temporary_dump"' EXIT
chmod 600 "$temporary_dump"
gpg --batch --pinentry-mode loopback --passphrase-file "$KEY_FILE" --decrypt --output "$temporary_dump" "$SOURCE"
if ! pg_restore --list "$temporary_dump" >/dev/null 2>&1; then
  echo "ERROR: decrypted backup is not a valid pg_dump archive" >&2
  exit 1
fi

dropdb -U "$PGUSER" -h "$PGHOST" --if-exists "$DATABASE"
createdb -U "$PGUSER" -h "$PGHOST" "$DATABASE"
pg_restore -U "$PGUSER" -h "$PGHOST" -d "$DATABASE" --no-owner --no-privileges "$temporary_dump"

printf 'Restore verified: %s -> %s\n' "$SOURCE" "$DATABASE"
