#!/usr/bin/env bash
set -euo pipefail

if [ "$(id -u)" -eq 0 ]; then
  run_privileged() { "$@"; }
elif command -v sudo >/dev/null 2>&1; then
  run_privileged() { sudo "$@"; }
else
  echo 'ERROR: run this script as root or with sudo' >&2
  exit 1
fi

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

run_privileged install -d -m 755 "$ROOT/releases"
temporary_release="$(run_privileged mktemp -d "$ROOT/releases/.${VERSION}.XXXXXX")"
previous="$(run_privileged readlink "$ROOT/current" 2>/dev/null || true)"
trap 'if [ -n "$temporary_release" ]; then run_privileged rm -rf "$temporary_release"; fi' EXIT

run_privileged cp -a "$SOURCE/." "$temporary_release/"
run_privileged chmod +x "$temporary_release/Statefalse.Api"
run_privileged mv "$temporary_release" "$RELEASE"
temporary_release=""
run_privileged chown -R root:statefalse "$RELEASE"
run_privileged chmod -R u=rwX,g=rX,o= "$RELEASE"
run_privileged chmod +x "$RELEASE/Statefalse.Api"

run_privileged ln -sfn "$RELEASE" "$ROOT/current"
if run_privileged systemctl restart statefalse && run_privileged "$ROOT/deploy/healthcheck.sh"; then
  printf 'Installed release %s\n' "$VERSION"
  exit 0
fi

echo "ERROR: release $VERSION failed healthcheck; restoring previous release" >&2
if [ -n "$previous" ]; then
  run_privileged ln -sfn "$previous" "$ROOT/current"
  run_privileged systemctl restart statefalse
else
  run_privileged rm -f "$ROOT/current"
  run_privileged systemctl stop statefalse
fi
exit 1
