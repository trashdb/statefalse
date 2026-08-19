#!/bin/bash
# Builds and launches a Debug app directly from DerivedData.
# This is for local development; it does not create or install a release.

set -euo pipefail

APP_NAME="Statefalse"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT="$SCRIPT_DIR/statefalse.xcodeproj"
DERIVED_DATA_PATH="${TMPDIR:-/tmp}/statefalse-derived-data"

echo "🔍 Building $APP_NAME (Debug)…"
xcodebuild \
    -project "$PROJECT" \
    -scheme "$APP_NAME" \
    -configuration Debug \
    -derivedDataPath "$DERIVED_DATA_PATH" \
    build

APP_BUNDLE="$DERIVED_DATA_PATH/Build/Products/Debug/$APP_NAME.app"
if [ ! -d "$APP_BUNDLE" ]; then
    echo "❌ Debug app not found at $APP_BUNDLE" >&2
    exit 1
fi

pkill -x "$APP_NAME" 2>/dev/null || true
open "$APP_BUNDLE"
echo "✅ Running local Debug build from $APP_BUNDLE"
