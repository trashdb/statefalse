#!/usr/bin/env bash
set -euo pipefail
cd /

PGDATABASE="${STATEFALSE_PGDATABASE:-statefalse}"
PGUSER="${STATEFALSE_PGUSER:-statefalse}"
PGHOST="${STATEFALSE_PGHOST:-localhost}"
BACKUP_DIR="${STATEFALSE_BACKUP_DIR:-/var/backups/statefalse}"
RETENTION_DAYS="${STATEFALSE_BACKUP_RETENTION_DAYS:-14}"

if ! command -v pg_dump >/dev/null 2>&1; then
  echo "ERROR: pg_dump is required" >&2
  exit 1
fi
if ! [[ "$RETENTION_DAYS" =~ ^[0-9]+$ ]] || [ "$RETENTION_DAYS" -lt 1 ]; then
  echo "ERROR: retention must be a positive integer" >&2
  exit 1
fi

install -d -m 700 "$BACKUP_DIR"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_path="$BACKUP_DIR/statefalse-$timestamp.dump"
tmp_path="$backup_path.tmp"
trap 'rm -f "$tmp_path"' EXIT

# pg_dump with custom format (-Fc) is safe to run while PostgreSQL is serving traffic.
pg_dump -U "$PGUSER" -d "$PGDATABASE" -h "$PGHOST" -Fc -f "$tmp_path"
if ! pg_restore --list "$tmp_path" >/dev/null 2>&1; then
  echo "ERROR: backup integrity check failed" >&2
  exit 1
fi

chmod 600 "$tmp_path"
mv "$tmp_path" "$backup_path"
find "$BACKUP_DIR" -type f -name 'statefalse-*.dump' -mtime "+$RETENTION_DAYS" -delete
printf 'Backup created: %s\n' "$backup_path"
