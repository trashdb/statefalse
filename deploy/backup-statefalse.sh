#!/usr/bin/env bash
set -euo pipefail
cd /

PGDATABASE="${STATEFALSE_PGDATABASE:-statefalse}"
PGUSER="${STATEFALSE_PGUSER:-statefalse}"
PGHOST="${STATEFALSE_PGHOST:-localhost}"
BACKUP_DIR="${STATEFALSE_BACKUP_DIR:-/var/backups/statefalse}"
RETENTION_DAYS="${STATEFALSE_BACKUP_RETENTION_DAYS:-14}"
KEY_FILE="${STATEFALSE_BACKUP_KEY_FILE:-/etc/statefalse/backup.key}"

for command_name in pg_dump pg_restore gpg install find mv date chmod rm stat; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "ERROR: $command_name is required" >&2
    exit 1
  fi
done
if [ ! -f "$KEY_FILE" ] || [ ! -r "$KEY_FILE" ]; then
  echo "ERROR: gpg and readable backup key are required" >&2
  exit 1
fi
key_mode="$(stat -c '%a' "$KEY_FILE" 2>/dev/null || stat -f '%Lp' "$KEY_FILE")"
if [ "$key_mode" != "600" ] && [ "$key_mode" != "640" ]; then
  echo "ERROR: backup key permissions must be 600 or root:postgres 640" >&2
  exit 1
fi
if ! [[ "$RETENTION_DAYS" =~ ^[0-9]+$ ]] || [ "$RETENTION_DAYS" -lt 1 ]; then
  echo "ERROR: retention must be a positive integer" >&2
  exit 1
fi

install -d -m 700 "$BACKUP_DIR"
timestamp="$(date -u +%Y%m%dT%H%M%SZ)"
backup_path="$BACKUP_DIR/statefalse-$timestamp.dump.gpg"
dump_path="$BACKUP_DIR/.statefalse-$timestamp.dump"
encrypted_path="$backup_path.tmp"
verify_path="$BACKUP_DIR/.statefalse-$timestamp.verify.dump"
trap 'rm -f "$dump_path" "$encrypted_path" "$verify_path"' EXIT

# pg_dump with custom format (-Fc) is safe to run while PostgreSQL is serving traffic.
pg_dump -U "$PGUSER" -d "$PGDATABASE" -h "$PGHOST" -Fc -f "$dump_path"
if ! pg_restore --list "$dump_path" >/dev/null 2>&1; then
  echo "ERROR: backup integrity check failed" >&2
  exit 1
fi

chmod 600 "$dump_path"
gpg --batch --yes --pinentry-mode loopback --passphrase-file "$KEY_FILE" --cipher-algo AES256 --symmetric --output "$encrypted_path" "$dump_path"
chmod 600 "$encrypted_path"
mv "$encrypted_path" "$backup_path"
rm -f "$dump_path"
gpg --batch --yes --pinentry-mode loopback --passphrase-file "$KEY_FILE" --decrypt --output "$verify_path" "$backup_path"
chmod 600 "$verify_path"
if ! pg_restore --list "$verify_path" >/dev/null 2>&1; then
  echo "ERROR: encrypted backup round-trip verification failed" >&2
  exit 1
fi
rm -f "$verify_path"
if [ ! -s "$backup_path" ]; then
  echo "ERROR: encrypted backup is empty" >&2
  exit 1
fi
find "$BACKUP_DIR" -type f -name 'statefalse-*.dump.gpg' -mtime "+$RETENTION_DAYS" -delete
printf 'Backup created: %s\n' "$backup_path"
