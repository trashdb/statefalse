#!/bin/bash
# Run this ONCE on the VPS via: ssh user@vps "sudo bash -s" < deploy/setup-vps.sh
set -euo pipefail

if [ "$(id -u)" -eq 0 ]; then
  run_privileged() { "$@"; }
  run_as_postgres() { runuser -u postgres -- "$@"; }
elif command -v sudo >/dev/null 2>&1; then
  run_privileged() { sudo "$@"; }
  run_as_postgres() { sudo -u postgres "$@"; }
else
  echo 'ERROR: run this script as root or with sudo' >&2
  exit 1
fi

APP_DIR="/opt/statefalse"
PGDATABASE="${STATEFALSE_PGDATABASE:-statefalse}"
PGUSER="${STATEFALSE_PGUSER:-statefalse}"

echo "=== Installing PostgreSQL ==="
if ! command -v psql >/dev/null 2>&1; then
  run_privileged apt-get update -qq
  run_privileged apt-get install -y -qq postgresql postgresql-client
fi

echo ""
echo "=== Creating database and user ==="
run_as_postgres psql -tc "SELECT 1 FROM pg_roles WHERE rolname='$PGUSER'" | grep -q 1 || \
  run_as_postgres psql -c "CREATE USER $PGUSER WITH PASSWORD 'CHANGE_ME';"
run_as_postgres psql -tc "SELECT 1 FROM pg_database WHERE datname='$PGDATABASE'" | grep -q 1 || \
  run_as_postgres psql -c "CREATE DATABASE $PGDATABASE OWNER $PGUSER;"
run_as_postgres psql -c "GRANT ALL PRIVILEGES ON DATABASE $PGDATABASE TO $PGUSER;"

echo ""
echo "=== Creating directories ==="
if ! id -u statefalse >/dev/null 2>&1; then
  sudo useradd --system --home-dir /nonexistent --no-create-home --shell /usr/sbin/nologin statefalse
fi
run_privileged mkdir -p "$APP_DIR"
run_privileged mkdir -p "$APP_DIR/releases" "$APP_DIR/deploy"
run_privileged mkdir -p /var/log/statefalse
run_privileged mkdir -p /var/backups/statefalse
run_privileged mkdir -p /var/www/statefalse
run_privileged chown root:root "$APP_DIR" "$APP_DIR/releases" "$APP_DIR/deploy"
run_privileged chmod 755 "$APP_DIR" "$APP_DIR/deploy"
run_privileged chmod 750 "$APP_DIR/releases"
if compgen -G "$APP_DIR/releases/*" >/dev/null; then
  run_privileged chown -R root:statefalse "$APP_DIR/releases"
  run_privileged chmod -R u=rwX,g=rX,o= "$APP_DIR/releases"
fi
run_privileged chown statefalse:statefalse /var/log/statefalse
run_privileged chmod 750 /var/log/statefalse
run_privileged chown postgres:postgres /var/backups/statefalse
run_privileged chmod 700 /var/backups/statefalse
run_privileged chmod 755 /var/www/statefalse

echo ""
echo "=== NEXT STEPS ==="
echo "1. Set the PostgreSQL password:"
echo "   sudo -u postgres psql -c \"ALTER USER $PGUSER PASSWORD 'YOUR_PASSWORD';\""
echo ""
echo "2. Update /etc/statefalse/statefalse.env with the connection string:"
echo "   DefaultConnection='Host=localhost;Database=$PGDATABASE;Username=$PGUSER;Password=YOUR_PASSWORD'"
echo ""
echo "3. Create the secrets directory (deploy.sh writes the env file here):"
echo "   sudo mkdir -p /etc/statefalse && sudo chown root:statefalse /etc/statefalse && sudo chmod 750 /etc/statefalse"
echo ""
echo "4. The public API is served by nginx at https://api.statefalse.com."
echo "   Nginx must proxy api.statefalse.com to 127.0.0.1:5000."
echo "   Install statefalse.service after DNS and TLS proxy setup:"
echo "   sudo cp deploy/statefalse.service          /etc/systemd/system/"
echo "   sudo systemctl daemon-reload"
echo "   sudo systemctl enable statefalse"
echo ""
echo "5. Deploy a versioned release from your Mac:"
echo "   ./deploy.sh user@TU_VPS_IP"
echo ""
echo "6. Start everything:"
echo "   sudo systemctl start statefalse"
echo "   sudo systemctl status statefalse"
echo ""
echo "7. Set up daily backups:"
echo "   sudo cp deploy/statefalse-backup.service deploy/statefalse-backup.timer /etc/systemd/system/"
echo "   sudo systemctl daemon-reload"
echo "   sudo systemctl enable --now statefalse-backup.timer"
