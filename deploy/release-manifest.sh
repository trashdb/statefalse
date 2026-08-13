#!/usr/bin/env bash
set -euo pipefail

OUTPUT="${1:?Usage: release-manifest.sh PUBLISH_DIR [OUTPUT_FILE]}"
MANIFEST="${2:-$OUTPUT/release-manifest.txt}"
VERSION="${STATEFALSE_VERSION:-$(git -C "$(dirname "$0")/.." rev-parse --short HEAD 2>/dev/null || date -u +%Y%m%dT%H%M%SZ)}"
printf 'version=%s\ncommit=%s\nbuilt_at=%s\n' "$VERSION" "$(git -C "$(dirname "$0")/.." rev-parse HEAD 2>/dev/null || echo unknown)" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" > "$MANIFEST"
sha256sum "$OUTPUT/Statefalse.Api" >> "$MANIFEST"
