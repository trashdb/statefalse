# Security policy

## Scope

Statefalse handles GitHub OAuth tokens, optional PATs, JWT sessions, webhook secrets and tenant-scoped GitHub data.

## Rules

- Never commit OAuth secrets, PATs, JWT secrets, webhook secrets, databases or production configuration.
- Production secrets live in `/etc/statefalse/statefalse.env` with mode `600` or a protected secret manager.
- Webhooks require `X-Hub-Signature-256`; invalid or unsigned requests fail closed.
- SignalR accepts JWT through `access_token` only because WebSocket clients cannot set Authorization headers. Reverse-proxy and application logs must redact query strings.
- Queries and notifications remain scoped to authenticated GitHub user and target IDs.
- Rotate OAuth, webhook and JWT secrets after exposure. JWT rotation invalidates existing sessions.
- PATs require minimum GitHub scopes and must never appear in logs, issue reports or payload fixtures.
- Use least-privilege deploy credentials and pinned SSH host keys.

## Incident response

1. Stop affected deployment or revoke exposed credential.
2. Preserve timestamps, commit SHA and redacted logs; never copy tokens into tickets.
3. Rotate credential and invalidate sessions where applicable.
4. Check webhook deliveries, audit log and database access.
5. Restore from verified backup only with explicit approval.
6. Record root cause and preventive change.

Report suspected vulnerabilities privately to repository owner. Do not open public issues containing secrets or exploit details.
