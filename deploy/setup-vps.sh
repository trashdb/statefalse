#!/bin/bash
# Run this ONCE on the VPS via: ssh user@vps "sudo bash -s" < deploy/setup-vps.sh
set -euo pipefail

APP_DIR="/opt/statefalse"

echo "=== Creating directory ==="
sudo mkdir -p "$APP_DIR"
sudo mkdir -p /var/lib/statefalse
sudo chmod 755 /var/lib/statefalse

echo ""
echo "=== NEXT STEP ==="
echo "Create the secrets directory (deploy.sh writes the env file here):"
echo "  sudo mkdir -p /etc/statefalse && sudo chmod 700 /etc/statefalse"
echo ""
echo "The public API is served by shared nginx at https://api.statefalse.com."
echo "Install statefalse.service after DNS and TLS proxy setup:"
echo "  sudo cp deploy/statefalse.service          /etc/systemd/system/"
echo "  sudo systemctl daemon-reload"
echo "  sudo systemctl enable statefalse"
echo ""
echo "Then deploy the binary from your Mac:"
echo "  ./deploy.sh user@TU_VPS_IP"
echo ""
echo "Finally start everything:"
echo "  sudo systemctl start statefalse"
echo "  sudo systemctl status statefalse"
