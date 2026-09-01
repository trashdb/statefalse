#!/usr/bin/env bash
# Run a command as root on the VPS, whether the SSH account is root or has sudo.
set -euo pipefail

if [ "$(id -u)" -eq 0 ]; then
  if [ "${1:-}" = "-u" ] && [ -n "${2:-}" ]; then
    user="$2"
    shift 2
    exec runuser -u "$user" -- "$@"
  fi
  exec "$@"
fi

if command -v sudo >/dev/null 2>&1; then
  exec sudo "$@"
fi

echo 'ERROR: this operation requires root or sudo' >&2
exit 1


