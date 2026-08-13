#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:?Usage: rollback-release.sh VERSION}"
ROOT="${STATEFALSE_RELEASE_ROOT:-/opt/statefalse}"
TARGET="$ROOT/releases/$VERSION"
[ -x "$TARGET/Statefalse.Api" ] || { echo "ERROR: release not found: $VERSION" >&2; exit 1; }
sudo ln -sfn "$TARGET" "$ROOT/current"
sudo systemctl restart statefalse
sudo "$ROOT/deploy/healthcheck.sh"
printf 'Rolled back to %s\n' "$VERSION"
