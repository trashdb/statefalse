# Statefalse administrator guide

This guide covers the configuration required to run the Statefalse backend. For the macOS app, start with the [user guide](USER-GUIDE.md).

## Prerequisites

- .NET runtime compatible with `backend/Statefalse.Api.csproj`.
- A running PostgreSQL instance with a dedicated database and user.
- A public HTTPS hostname for OAuth callbacks and GitHub webhooks.
- A GitHub OAuth App and a webhook secret.

## Required configuration

Do not put production credentials in tracked JSON files. Use environment variables, a protected environment file or a secret manager. The checked-in `deploy/statefalse.env.example` is a template only.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | PostgreSQL connection string (e.g. `Host=localhost;Database=statefalse;Username=...;Password=...`). |
| `GitHubOAuth__ClientId` | GitHub OAuth App client ID. |
| `GitHubOAuth__ClientSecret` | GitHub OAuth App client secret. |
| `GitHubOAuth__RedirectUri` | Exact public callback URL, normally `https://your-api.example.com/api/auth/callback`. |
| `WebhookSecret` | Secret shared by the backend and each GitHub webhook. |
| `Jwt__Secret` | Long random secret used to sign application sessions. |
| `Cors__AllowedOrigins__0` | Exact origin of the native client or web client when CORS configuration is required. |
| `WebhookLogs__AdminGitHubIds__0` | GitHub account ID allowed to inspect webhook logs. |

Generate independent secrets rather than reusing OAuth credentials:

```bash
openssl rand -hex 32
```

## OAuth App

Configure the GitHub OAuth App callback to exactly match `GitHubOAuth__RedirectUri`. The native app uses OAuth for sign-in; a personal access token is a separate, per-user credential used for actions that require additional GitHub permissions.

Keep the client secret and JWT secret out of source control. Rotate them if they are exposed, and restart the backend after changing them.

## 🔐 Authentication lifecycle

```text
GitHub OAuth → access JWT (default 1 h)
			 └→ opaque refresh token (default 30 d)
					└→ SHA-256 hash in PostgreSQL only
```

- `POST /api/v1/auth/refresh` rotates the refresh token on every successful
  request using an atomic conditional update. Concurrent requests with the
  same token produce at most one successful rotation. The presented token must
  never be stored in logs or database rows.
- `POST /api/v1/auth/logout` revokes the presented refresh token. It is an
  anonymous endpoint by design because the client may be unable to use an
  expired access JWT.
- The native client reacts to `401`, performs one refresh and retries the
  original request once. It does not perform a proactive background refresh.
- Access JWTs are stateless. **Logout revokes the refresh token but does not
  revoke already-issued access JWTs**; with the default configuration, one may
  remain valid for up to 1 hour. This is a known security boundary, not a
  guarantee of immediate logout invalidation.

### 🚨 Credential handling boundary

The backend persists the GitHub OAuth access token and optional user PAT in the
`GitHubUsers` record for backend-only GitHub API calls. These fields are
encrypted with AES-256-GCM before persistence; the encryption key is supplied
through the deployment environment as `GitHubCredentials__EncryptionKey` and
must be backed up separately from the database. The former
`/api/v1/auth/token` endpoint has been removed. Local Git pulls and pushes use
the repository's configured SSH agent or credential helper; Statefalse never
returns a GitHub credential to the native client. Protect the PostgreSQL database,
backups, logs and host access accordingly. Existing plaintext rows are migrated
idempotently during application startup.

SignalR authentication can place the JWT in the WebSocket `access_token` query
parameter. nginx API access logging is disabled, and the backend structured-log
enricher redacts authorization headers, bearer tokens, OAuth parameters, HMAC
headers and cookies. Application code must use structured logging and avoid
interpolating secrets directly into message text.

## Webhooks

Configure repository webhooks using the [webhook guide](WEBHOOKS.md). Use one secret per environment, verify delivery status in GitHub **Recent Deliveries**, and never log the secret or the `X-Hub-Signature-256` value.

## Deployment checklist

- [ ] HTTPS is enabled and the OAuth callback resolves publicly.
- [ ] Database and backup directories are writable only by their dedicated service accounts.
- [ ] OAuth client secret, webhook secret and JWT secret come from protected configuration.
- [ ] CORS allows only the required origins.
- [ ] Each monitored repository has an active webhook with the supported events.
- [ ] Logs and backups do not contain access tokens or webhook payload secrets.
- [ ] Backups and restore procedures have been tested.
- [x] WebSocket URLs are not persisted by nginx access logs; application-log
      review is still required before production.
- [x] The operator has documented the 1-hour access-token logout limitation.
- [ ] Existing GitHub OAuth/PAT credentials have an incident-response and
	  rotation procedure.
- [x] Refresh rotation has been tested with concurrent requests, not only a
      sequential happy path.

## Backup and restore verification

Backups are created locally by `statefalse-backup.timer` and encrypted with the
separate `Backup__EncryptionKey`. The isolated restore test is run by
`statefalse-restore-test.timer` as `postgres`; it restores the newest backup
into `statefalse_restore_test`, checks the resulting public-table count, and
always drops the temporary database. It never restores over the production
`statefalse` database.

Inspect the latest result with:

```bash
sudo systemctl status statefalse-restore-test.timer
sudo journalctl -u statefalse-restore-test.service -n 50 --no-pager
sudo systemctl start statefalse-restore-test.service
```

The current free/no-secondary-provider policy is: local retention is 14 days,
the target RPO is 24 hours, and the target RTO is 2 hours. This restore test
detects corrupt or unusable local backups, but it does not protect against a
complete loss of the VPS. Off-host replication requires a separate storage
destination and remains intentionally disabled.

For operational scripts, see `deploy/`. Review `SECURITY.md` before exposing a backend to the internet.

