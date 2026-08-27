#!/usr/bin/env bash
set -euo pipefail

SOURCE="${1:?Usage: restore-statefalse.sh BACKUP.dump DATABASE [--force]}"
DATABASE="${2:?Usage: restore-statefalse.sh BACKUP.dump DATABASE [--force]}"
FORCE="${3:-}"

PGUSER="${STATEFALSE_PGUSER:-statefalse}"
PGHOST="${STATEFALSE_PGHOST:-localhost}"

if [ ! -f "$SOURCE" ]; then
  echo "ERROR: backup not found: $SOURCE" >&2
  exit 1
fi
if ! pg_restore --list "$SOURCE" >/dev/null 2>&1; then
  echo "ERROR: not a valid pg_dump archive: $SOURCE" >&2
  exit 1
fi
if [ "$FORCE" != "--force" ]; then
  echo "ERROR: restore is destructive; pass --force after explicit approval" >&2
  exit 1
fi

echo "WARNING: This will drop and recreate the database '$DATABASE'." >&2
echo "Press Ctrl+C within 5 seconds to abort." >&2
sleep 5

dropdb -U "$PGUSER" -h "$PGHOST" --if-exists "$DATABASE"
createdb -U "$PGUSER" -h "$PGHOST" "$DATABASE"
pg_restore -U "$PGUSER" -h "$PGHOST" -d "$DATABASE" --no-owner --no-privileges "$SOURCE"

printf 'Restore verified: %s -> %s\n' "$SOURCE" "$DATABASE"
