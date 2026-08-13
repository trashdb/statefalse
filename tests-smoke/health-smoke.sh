#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://127.0.0.1:5000}"
response="$(curl --fail --silent --show-error --max-time 10 "$BASE_URL/health")"
printf '%s' "$response" | grep -q '"status":"healthy"'
printf '%s' "$response" | grep -q '"database":true'
printf '%s\n' "Health smoke passed: $BASE_URL"
