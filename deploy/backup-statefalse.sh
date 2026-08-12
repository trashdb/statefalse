#!/usr/bin/env bash
set -euo pipefail

DB_PATH="${STATEFALSE_DB_PATH:-/var/lib/statefalse/statefalse.db}"
BACKUP_DIR="${STATEFALSE_BACKUP_DIR:-/var/backups/statefalse}"
RETENTION_DAYS="${STATEFALSE_BACKUP_RETENTION_DAYS:-14}"

if ! command -v sqlite3 >/dev/null 2>&1; then
  echo "ERROR: sqlite3 is required" >&2
  exit 1
fi
if [ ! -f "$DB_PATH" ]; then
  echo "ERROR: database not found: $DB_PATH" >&2
  exit 1
fi
if ! [[ "$RETENTION_DAYS" =~ ^[0-9]+$ ]] || [ "$RETENTION_DAYS" -lt 1 ]; then
  echo "ERROR: retention must be a positive integer" >&2
  exit 1
fi

install -d -m 700 "$BACKUP_DIR"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_path="$BACKUP_DIR/statefalse-$timestamp.db"
tmp_path="$backup_path.tmp"
trap 'rm -f "$tmp_path"' EXIT

# .backup is safe while SQLite is serving traffic, including WAL mode.
sqlite3 "$DB_PATH" ".timeout 5000" ".backup '$tmp_path'"
integrity="$(sqlite3 "$tmp_path" 'PRAGMA integrity_check;')"
if [ "$integrity" != "ok" ]; then
  echo "ERROR: backup integrity check failed: $integrity" >&2
  exit 1
fi

chmod 600 "$tmp_path"
mv "$tmp_path" "$backup_path"
find "$BACKUP_DIR" -type f -name 'statefalse-*.db' -mtime "+$RETENTION_DAYS" -delete
printf 'Backup created: %s\n' "$backup_path"

