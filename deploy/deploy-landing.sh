#!/usr/bin/env bash
# Publish the static landing from your Mac: ./deploy/deploy-landing.sh user@vps-ip
set -euo pipefail

VPS="${1:?Usage: ./deploy/deploy-landing.sh user@vps-ip}"
SCRIPT_DIR="$(cd "$(dirname "$0")/.." && pwd)"
REMOTE_LANDING="/var/www/statefalse"
REMOTE_NGINX="/etc/nginx/conf.d/statefalse-api.conf"
REMOTE_TMP="/tmp/statefalse-landing"

if [ ! -f "$SCRIPT_DIR/landing/index.html" ]; then
  echo "ERROR: landing/index.html not found. Run this script from the repository." >&2
  exit 1
fi

echo "=== Uploading landing ==="
rsync -az --delete --exclude='README.md' "$SCRIPT_DIR/landing/" "$VPS:$REMOTE_TMP/"
ssh "$VPS" "sudo install -d -m 755 $REMOTE_LANDING && sudo rsync -az --delete $REMOTE_TMP/ $REMOTE_LANDING/ && sudo chown -R root:root $REMOTE_LANDING && sudo find $REMOTE_LANDING -type d -exec chmod 755 {} + && sudo find $REMOTE_LANDING -type f -exec chmod 644 {} +"

echo "=== Installing nginx configuration ==="
scp "$SCRIPT_DIR/deploy/nginx/statefalse-api.conf" "$VPS:/tmp/statefalse-api.conf"
ssh "$VPS" "sudo cp $REMOTE_NGINX $REMOTE_NGINX.bak && sudo cp /tmp/statefalse-api.conf $REMOTE_NGINX && if sudo nginx -t; then sudo systemctl reload nginx; else sudo cp $REMOTE_NGINX.bak $REMOTE_NGINX; echo 'ERROR: nginx config rejected; previous config restored.' >&2; exit 1; fi"

echo "=== Verifying landing ==="
curl --fail --silent --show-error --max-time 15 https://statefalse.com/ | grep -q 'Statefalse'
echo "Landing is live at https://statefalse.com/"
