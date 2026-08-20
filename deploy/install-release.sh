#!/usr/bin/env bash
set -euo pipefail

SOURCE="${1:?Usage: install-release.sh PUBLISH_DIR VERSION}"
VERSION="${2:?Usage: install-release.sh PUBLISH_DIR VERSION}"
ROOT="${STATEFALSE_RELEASE_ROOT:-/opt/statefalse}"
RELEASE="$ROOT/releases/$VERSION"

case "$VERSION" in
  ''|.|..|*[!A-Za-z0-9._-]*)
	echo "ERROR: invalid release version: $VERSION" >&2
	exit 1
	;;
esac

[ -d "$SOURCE" ] || { echo "ERROR: publish directory not found: $SOURCE" >&2; exit 1; }
[ -x "$SOURCE/Statefalse.Api" ] || { echo 'ERROR: publish binary missing or not executable' >&2; exit 1; }
[ -x "$ROOT/deploy/healthcheck.sh" ] || { echo "ERROR: missing executable healthcheck: $ROOT/deploy/healthcheck.sh" >&2; exit 1; }
[ ! -e "$RELEASE" ] || { echo "ERROR: release already exists: $VERSION" >&2; exit 1; }

sudo install -d -m 755 "$ROOT/releases"
temporary_release="$(sudo mktemp -d "$ROOT/releases/.${VERSION}.XXXXXX")"
previous="$(sudo readlink "$ROOT/current" 2>/dev/null || true)"
trap 'if [ -n "$temporary_release" ]; then sudo rm -rf "$temporary_release"; fi' EXIT

sudo cp -a "$SOURCE/." "$temporary_release/"
sudo chmod +x "$temporary_release/Statefalse.Api"
sudo mv "$temporary_release" "$RELEASE"
temporary_release=""

sudo ln -sfn "$RELEASE" "$ROOT/current"
if sudo systemctl restart statefalse && sudo "$ROOT/deploy/healthcheck.sh"; then
  printf 'Installed release %s\n' "$VERSION"
  exit 0
fi

echo "ERROR: release $VERSION failed healthcheck; restoring previous release" >&2
if [ -n "$previous" ]; then
  sudo ln -sfn "$previous" "$ROOT/current"
  sudo systemctl restart statefalse
else
  sudo rm -f "$ROOT/current"
fi
exit 1
