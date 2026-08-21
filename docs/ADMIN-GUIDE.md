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

For operational scripts, see `deploy/`. Review `SECURITY.md` before exposing a backend to the internet.

