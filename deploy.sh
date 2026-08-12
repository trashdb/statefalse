#!/bin/bash
# Run from your Mac: ./deploy.sh user@vps-ip
set -euo pipefail

VPS="${1:?Usage: ./deploy.sh user@vps-ip}"
REMOTE="/opt/statefalse"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

cd "$SCRIPT_DIR/backend"

PUBLISH_DIR="$(mktemp -d "${TMPDIR:-/tmp}/statefalse-publish.XXXXXX")"
trap 'rm -rf "$PUBLISH_DIR"' EXIT

if [ -f "$SCRIPT_DIR/deploy/statefalse.env" ]; then
  for required in WebhookSecret GitHubOAuth__ClientId GitHubOAuth__ClientSecret Jwt__Secret; do
    value="$(awk -F= -v key="$required" '$1 == key { print substr($0, index($0, "=") + 1); exit }' "$SCRIPT_DIR/deploy/statefalse.env")"
    if [ -z "$value" ] || [ "$value" = "CHANGE_ME" ] || [ "$value" = "YOUR_GITHUB_CLIENT_ID" ] || [ "$value" = "YOUR_GITHUB_CLIENT_SECRET" ]; then
      echo "ERROR: deploy/statefalse.env contains placeholder or missing $required. Refusing deployment."
      exit 1
    fi
  done
  jwt_length="$(awk -F= '$1 == "Jwt__Secret" { print length(substr($0, index($0, "=") + 1)); exit }' "$SCRIPT_DIR/deploy/statefalse.env")"
  if [ "$jwt_length" -lt 32 ]; then
    echo "ERROR: Jwt__Secret must contain at least 32 characters. Refusing deployment."
    exit 1
  fi
else
  echo "ERROR: deploy/statefalse.env not found. Refusing deployment to protect production environment."
  exit 1
fi

echo "=== Building self-contained binary ==="
dotnet publish -c Release --self-contained true -r linux-x64 -o "$PUBLISH_DIR"

echo "=== Uploading to VPS ==="
ssh "$VPS" "sudo mkdir -p $REMOTE"
rsync -az --delete --exclude='*.db' "$PUBLISH_DIR/" "$VPS:$REMOTE/"
rsync -az "$SCRIPT_DIR/deploy/" "$VPS:$REMOTE/deploy/"

echo "=== Installing systemd unit ==="
ssh "$VPS" "sudo cp $REMOTE/deploy/statefalse.service /etc/systemd/system/ && sudo systemctl daemon-reload"

echo "=== Installing environment file ==="
ssh "$VPS" "sudo mkdir -p /etc/statefalse && sudo chmod 700 /etc/statefalse && sudo tee /etc/statefalse/statefalse.env >/dev/null && sudo chmod 600 /etc/statefalse/statefalse.env" < "$SCRIPT_DIR/deploy/statefalse.env"
echo "Installed /etc/statefalse/statefalse.env (mode 600)"

echo "=== Copying production config ==="
if [ -f appsettings.Production.json ]; then
  scp appsettings.Production.json "$VPS:$REMOTE/"
else
  echo "WARNING: appsettings.Production.json not found"
fi

echo "=== Setting permissions ==="
ssh "$VPS" "sudo chmod +x $REMOTE/Statefalse.Api"

echo "=== Restarting service ==="
ssh "$VPS" "sudo systemctl daemon-reload && sudo systemctl restart statefalse"

echo ""
echo "=== Done! ==="
echo "Logs: ssh $VPS 'sudo journalctl -u statefalse -f'"
echo "Status: ssh $VPS 'sudo systemctl status statefalse'"
