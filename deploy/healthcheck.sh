#!/usr/bin/env bash
set -euo pipefail

URL="${1:-http://127.0.0.1:5000/health}"
response="$(curl --fail --silent --show-error --max-time 15 "$URL")"
printf '%s' "$response" | grep -q '"status":"healthy"'
printf '%s' "$response" | grep -q '"database":true'
printf '%s\n' "Health OK: $URL"
