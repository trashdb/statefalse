#!/usr/bin/env bash
set -euo pipefail

URL="${1:-http://127.0.0.1:5000/health}"
RETRIES="${HEALTHCHECK_RETRIES:-30}"
DELAY="${HEALTHCHECK_DELAY:-2}"

for attempt in $(seq 1 "$RETRIES"); do
  if response="$(curl --fail --silent --show-error --max-time 15 "$URL" 2>/dev/null)" \
	&& printf '%s' "$response" | grep -Eq '"status"[[:space:]]*:[[:space:]]*"healthy"' \
	&& printf '%s' "$response" | grep -Eq '"database"[[:space:]]*:[[:space:]]*true'; then
	printf '%s\n' "Health OK: $URL"
	exit 0
  fi

  if [ "$attempt" -lt "$RETRIES" ]; then
	sleep "$DELAY"
  fi
done

echo "ERROR: health check failed after $RETRIES attempts: $URL" >&2
exit 1
