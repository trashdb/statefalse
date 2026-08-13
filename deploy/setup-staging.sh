#!/usr/bin/env bash
set -euo pipefail

sudo install -d -m 755 /opt/statefalse-staging /opt/statefalse-staging/releases /var/lib/statefalse-staging /var/backups/statefalse-staging
sudo install -d -m 700 /etc/statefalse
sudo install -m 644 "$(dirname "$0")/statefalse-staging.service" /etc/systemd/system/statefalse-staging.service
sudo systemctl daemon-reload
printf '%s\n' 'Staging directories and unit installed. Create /etc/statefalse/staging.env with unique staging secrets before enabling service.'
