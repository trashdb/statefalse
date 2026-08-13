# Operations

## Production topology

`statefalse.com` redirects to `api.statefalse.com`; Cloudflare and shared Docker Nginx terminate TLS and proxy to Kestrel on `172.18.0.1:5000`. `statefalse.service` runs the backend. SQLite lives at `/var/lib/statefalse/statefalse.db`. Ngrok is disabled.

## Routine checks

```bash
curl -fsS https://api.statefalse.com/health
ssh underlayer 'systemctl is-active statefalse statefalse-backup.timer'
ssh underlayer 'sudo journalctl -u statefalse --since "30 minutes ago" --no-pager'
```

## Deploy

Run `./deploy.sh underlayer` only from reviewed code. It restores, publishes, excludes secrets, creates a SQLite backup, installs the backup timer and restarts systemd. Verify health and SignalR after deploy.

## Backup and restore

- Daily timer: `statefalse-backup.timer`.
- Backup path: `/var/backups/statefalse`.
- Current retention: 14 days.
- Integrity check: `PRAGMA integrity_check`.
- Restore tool: `/opt/statefalse/deploy/restore-statefalse.sh BACKUP.db DESTINATION.db`.
- Never replace active production DB without an approved maintenance window, a fresh backup and a restore test.
- Off-host backup and RPO/RTO remain pending.

## Incident runbooks

- **API down:** check `systemctl status statefalse`, journal logs, disk space and `/health`; do not delete the database.
- **Webhook rejected:** inspect GitHub delivery status and compare secret configuration without printing either value.
- **SignalR disconnected:** check Nginx WebSocket upgrade directives, WSS requests and `SignalRConnectionId` registration.
- **Deploy failed:** keep previous process running when possible; inspect publish and systemd logs before retrying.
- **Database issue:** stop writes, preserve DB and WAL files, create forensic copy, then restore only from verified backup.

## Staging prerequisite

Staging requires separate VPS or service, database, OAuth App, webhook secret, JWT secret and hostname. Never reuse production credentials or data.
