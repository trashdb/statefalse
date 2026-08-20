#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?Usage: rollback-release.sh VERSION}"
ROOT="${STATEFALSE_RELEASE_ROOT:-/opt/statefalse}"
TARGET="$ROOT/releases/$VERSION"

case "$VERSION" in
  ''|.|..|*[!A-Za-z0-9._-]*)
    echo "ERROR: invalid release version: $VERSION" >&2
    exit 1
    ;;
esac

[ -x "$TARGET/Statefalse.Api" ] || { echo "ERROR: release not found or invalid: $VERSION" >&2; exit 1; }
[ -x "$ROOT/deploy/healthcheck.sh" ] || { echo "ERROR: missing executable healthcheck: $ROOT/deploy/healthcheck.sh" >&2; exit 1; }

previous="$(sudo readlink "$ROOT/current" 2>/dev/null || true)"
sudo ln -sfn "$TARGET" "$ROOT/current"
if sudo systemctl restart statefalse && sudo "$ROOT/deploy/healthcheck.sh"; then
  printf 'Rolled back to %s\n' "$VERSION"
  exit 0
fi

echo "ERROR: rollback to $VERSION failed; restoring previous release" >&2
if [ -n "$previous" ]; then
  sudo ln -sfn "$previous" "$ROOT/current"
  sudo systemctl restart statefalse
fi
exit 1
