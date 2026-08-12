#!/usr/bin/env bash
set -euo pipefail

SOURCE="${1:?Usage: restore-statefalse.sh BACKUP.db DESTINATION.db [--force]}"
DESTINATION="${2:?Usage: restore-statefalse.sh BACKUP.db DESTINATION.db [--force]}"
FORCE="${3:-}"

if [ ! -f "$SOURCE" ]; then
  echo "ERROR: backup not found: $SOURCE" >&2
  exit 1
fi
if [ -e "$DESTINATION" ] && [ "$FORCE" != "--force" ]; then
  echo "ERROR: destination exists; use --force only after explicit approval: $DESTINATION" >&2
  exit 1
fi

mkdir -p "$(dirname "$DESTINATION")"
tmp_path="$DESTINATION.restore.tmp"
trap 'rm -f "$tmp_path"' EXIT
sqlite3 "$SOURCE" 'PRAGMA integrity_check;' | grep -qx 'ok'
sqlite3 "$SOURCE" ".backup '$tmp_path'"
sqlite3 "$tmp_path" 'PRAGMA integrity_check;' | grep -qx 'ok'
chmod 600 "$tmp_path"
mv "$tmp_path" "$DESTINATION"
printf 'Restore verified: %s\n' "$DESTINATION"

