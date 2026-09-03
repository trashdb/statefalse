#!/bin/bash
# Run from your Mac: ./deploy.sh user@vps-ip
set -euo pipefail

VPS="${1:?Usage: ./deploy.sh user@vps-ip}"
REMOTE="/opt/statefalse"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VERSION="${STATEFALSE_VERSION:-$(git -C "$SCRIPT_DIR" describe --tags --exact-match HEAD 2>/dev/null || git -C "$SCRIPT_DIR" rev-parse --short HEAD)}"
REMOTE_UPLOAD="/tmp/statefalse-publish-${VERSION}-$$"

case "$VERSION" in
  ''|.|..|*[!A-Za-z0-9._-]*)
    echo "ERROR: invalid release version: $VERSION" >&2
    exit 1
    ;;
esac

cd "$SCRIPT_DIR/backend"

PUBLISH_DIR="$(mktemp -d "${TMPDIR:-/tmp}/statefalse-publish.XXXXXX")"
trap 'rm -rf "$PUBLISH_DIR"' EXIT

if [ -f "$SCRIPT_DIR/deploy/statefalse.env" ]; then
  for required in ConnectionStrings__DefaultConnection WebhookSecret GitHubOAuth__ClientId GitHubOAuth__ClientSecret Jwt__Secret GitHubCredentials__EncryptionKey Backup__EncryptionKey; do
    value="$(awk -F= -v key="$required" '$1 == key { print substr($0, index($0, "=") + 1); exit }' "$SCRIPT_DIR/deploy/statefalse.env")"
    if [ -z "$value" ] || [ "$value" = "CHANGE_ME" ] || [ "$value" = "CHANGE_ME_BASE64_256_BIT_KEY" ] || [ "$value" = "YOUR_GITHUB_CLIENT_ID" ] || [ "$value" = "YOUR_GITHUB_CLIENT_SECRET" ]; then
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
dotnet restore
dotnet publish -c Release --self-contained true -r linux-x64 -o "$PUBLISH_DIR"

if [ -f appsettings.Production.json ]; then
  cp appsettings.Production.json "$PUBLISH_DIR/"
fi

echo "=== Uploading to VPS ==="
# shellcheck disable=SC2029 # REMOTE intentionally expands in the local shell.
ssh "$VPS" "if [ \$(id -u) -eq 0 ]; then mkdir -p $REMOTE/releases $REMOTE/deploy; elif command -v sudo >/dev/null 2>&1; then sudo mkdir -p $REMOTE/releases $REMOTE/deploy; else echo 'ERROR: VPS account must be root or have sudo' >&2; exit 1; fi"
rsync -az --exclude='statefalse.env' "$SCRIPT_DIR/deploy/" "$VPS:$REMOTE/deploy/"
rsync -az "$PUBLISH_DIR/" "$VPS:$REMOTE_UPLOAD/"
trap 'ssh "$VPS" "rm -rf $REMOTE_UPLOAD" >/dev/null 2>&1 || true; rm -rf "$PUBLISH_DIR"' EXIT

echo "=== Installing backup timer ==="
# shellcheck disable=SC2029 # REMOTE intentionally expands in the local shell.
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" mkdir -p /var/backups/statefalse /var/log/statefalse
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" chown postgres:postgres /var/backups/statefalse
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" chmod 700 /var/backups/statefalse
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" chown statefalse:statefalse /var/log/statefalse
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" chmod 750 /var/log/statefalse
  # shellcheck disable=SC2029 # REMOTE intentionally expands in the local shell.
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" chmod 755 "$REMOTE" "$REMOTE/deploy"
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" chmod +x "$REMOTE/deploy/"*.sh
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" cp "$REMOTE/deploy/statefalse-backup.service" /etc/systemd/system/
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" cp "$REMOTE/deploy/statefalse-backup.timer" /etc/systemd/system/
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" cp "$REMOTE/deploy/statefalse-restore-test.service" /etc/systemd/system/
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" cp "$REMOTE/deploy/statefalse-restore-test.timer" /etc/systemd/system/
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" systemctl daemon-reload
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" systemctl enable statefalse-backup.timer
  ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" systemctl enable --now statefalse-restore-test.timer

echo "=== Installing backup key ==="
backup_key="$(awk -F= '$1 == "Backup__EncryptionKey" { print substr($0, index($0, "=") + 1); exit }' "$SCRIPT_DIR/deploy/statefalse.env")"
# shellcheck disable=SC2029 # The remote-sudo command intentionally expands in the local shell.
printf '%s\n' "$backup_key" | ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh sh -c 'mkdir -p /etc/statefalse && tee /etc/statefalse/backup.key >/dev/null && chown root:postgres /etc/statefalse/backup.key && chmod 640 /etc/statefalse/backup.key'"
unset backup_key
echo "Installed /etc/statefalse/backup.key (mode 640, root:postgres)"

echo "=== Backing up database ==="
# shellcheck disable=SC2029 # REMOTE intentionally expands in the local shell.
    ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" -u postgres bash "$REMOTE/deploy/backup-statefalse.sh"
    ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" systemctl start statefalse-backup.timer

echo "=== Installing systemd unit ==="
# shellcheck disable=SC2029 # REMOTE intentionally expands in the local shell.
ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" cp "$REMOTE/deploy/statefalse.service" /etc/systemd/system/
ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" systemctl daemon-reload

echo "=== Installing environment file ==="
# shellcheck disable=SC2029 # The remote-sudo command intentionally expands in the local shell.
ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh sh -c 'mkdir -p /etc/statefalse && tee /etc/statefalse/statefalse.env >/dev/null && chown root:statefalse /etc/statefalse/statefalse.env && chmod 640 /etc/statefalse/statefalse.env'" < "$SCRIPT_DIR/deploy/statefalse.env"
echo "Installed /etc/statefalse/statefalse.env (mode 640, root:statefalse)"

echo "=== Copying production config ==="
if [ -f appsettings.Production.json ]; then
  echo "Production config included in release $VERSION"
else
  echo "WARNING: appsettings.Production.json not found"
fi

echo "=== Installing release $VERSION ==="
# shellcheck disable=SC2029 # REMOTE and VERSION intentionally expand in the local shell.
ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" "$REMOTE/deploy/install-release.sh" "$REMOTE_UPLOAD" "$VERSION" && ssh "$VPS" rm -rf "$REMOTE_UPLOAD"

echo "=== Verifying service ==="
# shellcheck disable=SC2029 # REMOTE intentionally expands in the local shell.
ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" systemctl is-active --quiet statefalse
# shellcheck disable=SC2029 # REMOTE intentionally expands in the local shell.
ssh "$VPS" "$REMOTE/deploy/remote-sudo.sh" "$REMOTE/deploy/healthcheck.sh"

echo ""
echo "=== Done! ==="
echo "Release: $VERSION"
echo "Logs: ssh $VPS 'sudo journalctl -u statefalse -f'"
echo "Status: ssh $VPS 'sudo systemctl status statefalse'"
