# Statefalse administrator guide

This guide covers the configuration required to run the Statefalse backend. For the macOS app, start with the [user guide](USER-GUIDE.md).

## Prerequisites

- .NET runtime compatible with `backend/Statefalse.Api.csproj`.
- A persistent SQLite data directory with restricted permissions.
- A public HTTPS hostname for OAuth callbacks and GitHub webhooks.
- A GitHub OAuth App and a webhook secret.

## Required configuration

Do not put production credentials in tracked JSON files. Use environment variables, a protected environment file or a secret manager. The checked-in `deploy/statefalse.env.example` is a template only.

| Setting | Purpose |
|---|---|
| `ConnectionStrings__DefaultConnection` | Persistent SQLite database path. |
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
GitHub OAuth → access JWT (default 12 h)
			 └→ opaque refresh token (default 30 d)
					└→ SHA-256 hash in SQLite only
```

- `POST /api/v1/auth/refresh` rotates the refresh token on every successful
  request. The presented token must never be stored in logs or database rows.
- `POST /api/v1/auth/logout` revokes the presented refresh token. It is an
  anonymous endpoint by design because the client may be unable to use an
  expired access JWT.
- The native client reacts to `401`, performs one refresh and retries the
  original request once. It does not perform a proactive background refresh.
- Access JWTs are stateless. **Logout revokes the refresh token but does not
  revoke already-issued access JWTs**; with the default configuration, one may
  remain valid for up to 12 hours. This is a known security boundary, not a
  guarantee of immediate logout invalidation.

### 🚨 Credential handling boundary

The backend currently persists the GitHub OAuth access token and optional user
PAT in the `GitHubUsers` record, and `/api/v1/auth/token` returns the selected
credential to an authenticated client. Protect the SQLite database, backups,
logs and host access accordingly. Do not claim that database compromise or a
valid Statefalse JWT cannot expose a reusable GitHub credential. A production
hardening project should remove this token-return endpoint and encrypt or move
GitHub credentials to an external secret store before treating this area as
closed.

SignalR authentication can place the JWT in the WebSocket `access_token` query
parameter. Configure the reverse proxy and application logging to redact query
strings and avoid recording full WebSocket URLs.

## Webhooks

Configure repository webhooks using the [webhook guide](WEBHOOKS.md). Use one secret per environment, verify delivery status in GitHub **Recent Deliveries**, and never log the secret or the `X-Hub-Signature-256` value.

## Deployment checklist

- [ ] HTTPS is enabled and the OAuth callback resolves publicly.
- [ ] Database and backup directories are writable only by the service account.
- [ ] OAuth client secret, webhook secret and JWT secret come from protected configuration.
- [ ] CORS allows only the required origins.
- [ ] Each monitored repository has an active webhook with the supported events.
- [ ] Logs and backups do not contain access tokens or webhook payload secrets.
- [ ] Backups and restore procedures have been tested.
- [ ] WebSocket URLs are redacted in reverse-proxy and application logs.
- [ ] The operator has documented the 12-hour access-token logout limitation.
- [ ] Existing GitHub OAuth/PAT credentials have an incident-response and
	  rotation procedure.
- [ ] Refresh rotation has been tested with concurrent requests, not only a
	  sequential happy path.

For operational scripts, see `deploy/`. Review `SECURITY.md` before exposing a backend to the internet.

