#!/usr/bin/env bash
# Download and install one published Statefalse release.
# Usage: ./install-release.sh v0.2.5

set -euo pipefail

VERSION="${1:?Usage: $0 vMAJOR.MINOR.PATCH}"
if [[ ! "$VERSION" =~ ^v[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
    echo "ERROR: version must use vMAJOR.MINOR.PATCH (for example, v0.2.5)." >&2
    exit 1
fi

APP_NAME="Statefalse"
INSTALL_DIR="$HOME/Applications"
APP_BUNDLE="$INSTALL_DIR/$APP_NAME.app"
DOWNLOAD_DIR="$HOME/Downloads/statefalse-$VERSION"
ARCHIVE="$DOWNLOAD_DIR/$APP_NAME-$VERSION.zip"
CHECKSUMS="$DOWNLOAD_DIR/SHA256SUMS"
EXTRACT_DIR="$(mktemp -d "${TMPDIR:-/tmp}/statefalse-release.XXXXXX")"

cleanup() {
    rm -rf "$EXTRACT_DIR"
}
trap cleanup EXIT

BASE_URL="https://github.com/trashdb/statefalse/releases/download/$VERSION"
mkdir -p "$DOWNLOAD_DIR"
cd "$DOWNLOAD_DIR"

echo "Downloading Statefalse $VERSION…"
curl -fL -o "$ARCHIVE" "$BASE_URL/$APP_NAME-$VERSION.zip"
curl -fL -o "$CHECKSUMS" "$BASE_URL/SHA256SUMS"

echo "Verifying checksum…"
shasum -a 256 -c "$CHECKSUMS"

echo "Extracting verified release…"
ditto -x -k "$ARCHIVE" "$EXTRACT_DIR"
test -d "$EXTRACT_DIR/$APP_NAME.app"

# Do not remove an installed app until the new archive has passed verification
# and extraction.
if pgrep -x "$APP_NAME" >/dev/null 2>&1; then
    echo "Closing the previous Statefalse instance…"
    pkill -x "$APP_NAME" 2>/dev/null || true
    sleep 1
fi

mkdir -p "$INSTALL_DIR"
rm -rf "$APP_BUNDLE"
rm -rf "$INSTALL_DIR"/Statefalse.app.backup-*

# Remove extracted app copies and local development products that Spotlight could
# offer as separate installations. Downloaded ZIPs and checksums are preserved.
rm -rf "$HOME/Downloads/Statefalse.app"
find "$HOME/Downloads" -type d -path '*/statefalse-v*/Statefalse.app' -prune -exec rm -rf {} + 2>/dev/null || true
find "$HOME/Library/Developer/Xcode/DerivedData" -type d -name Statefalse.app -prune -exec rm -rf {} + 2>/dev/null || true

xattr -dr com.apple.quarantine "$EXTRACT_DIR/$APP_NAME.app" 2>/dev/null || true
ditto "$EXTRACT_DIR/$APP_NAME.app" "$APP_BUNDLE"

LSREGISTER="/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister"
"$LSREGISTER" -f "$APP_BUNDLE" 2>/dev/null || true

if [ -d "/Applications/$APP_NAME.app" ]; then
    echo "WARNING: /Applications/$APP_NAME.app still exists. Remove it manually with administrator privileges if Spotlight lists it." >&2
fi

echo "Installed: $APP_BUNDLE"
echo "Opening Statefalse $VERSION…"
open "$APP_BUNDLE"

