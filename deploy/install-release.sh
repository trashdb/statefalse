#!/usr/bin/env bash
set -euo pipefail

SOURCE="${1:?Usage: install-release.sh PUBLISH_DIR VERSION}"
VERSION="${2:?Usage: install-release.sh PUBLISH_DIR VERSION}"
ROOT="${STATEFALSE_RELEASE_ROOT:-/opt/statefalse}"
RELEASE="$ROOT/releases/$VERSION"

[ -x "$SOURCE/Statefalse.Api" ] || { echo 'ERROR: publish binary missing or not executable' >&2; exit 1; }
sudo install -d -m 755 "$RELEASE"
sudo cp -a "$SOURCE/." "$RELEASE/"
sudo ln -sfn "$RELEASE" "$ROOT/current"
sudo systemctl restart statefalse
sudo "$ROOT/deploy/healthcheck.sh"
