#!/bin/bash
# Run from your Mac: ./deploy.sh user@vps-ip
set -euo pipefail

VPS="${1:?Usage: ./deploy.sh user@vps-ip}"
REMOTE="/opt/statefalse"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

cd "$SCRIPT_DIR/backend"

echo "=== Building self-contained binary ==="
dotnet publish -c Release --self-contained true -r linux-x64 -o ./publish

echo "=== Uploading to VPS ==="
ssh "$VPS" "sudo mkdir -p $REMOTE"
rsync -az --delete --exclude='*.db' ./publish/ "$VPS:$REMOTE/"
rsync -az "$SCRIPT_DIR/deploy/" "$VPS:$REMOTE/deploy/"

echo "=== Installing systemd units ==="
ssh "$VPS" "sudo cp $REMOTE/deploy/statefalse.service /etc/systemd/system/ && sudo cp $REMOTE/deploy/statefalse-tunnel.service /etc/systemd/system/ && sudo systemctl daemon-reload"

echo "=== Installing environment file ==="
if [ -f "$SCRIPT_DIR/deploy/statefalse.env" ]; then
  ssh "$VPS" "sudo mkdir -p /etc/statefalse && sudo chmod 700 /etc/statefalse && sudo tee /etc/statefalse/statefalse.env >/dev/null && sudo chmod 600 /etc/statefalse/statefalse.env" < "$SCRIPT_DIR/deploy/statefalse.env"
  echo "Installed /etc/statefalse/statefalse.env (mode 600)"
else
  echo "WARNING: deploy/statefalse.env not found - service will fail to start."
  echo "Copy deploy/statefalse.env.example to deploy/statefalse.env and fill in real values."
fi

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
