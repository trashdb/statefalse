# Security policy

Statefalse is currently an early-stage application. Do not include access tokens, passwords, private repository data or other sensitive information in issues or pull requests.

## Report a vulnerability

Report suspected vulnerabilities privately to the repository owner. Do not open public issues containing secrets, personal data or exploit details.

Include, when safe to share:

- Affected version or commit.
- Operating system and environment.
- Minimal reproduction steps.
- Expected and actual behavior.
- Suggested mitigation, if known.

Allow reasonable time for investigation and remediation before public disclosure. Do not test against accounts, repositories or systems that you do not own or have permission to assess.

## 🔐 Authentication security notes

Statefalse uses two different credential layers:

```text
GitHub credential  ──► backend integration / requested GitHub actions
Statefalse JWT     ──► API authorization (default lifetime: 1 h)
Refresh token      ──► one-time session renewal (default lifetime: 30 d)
```

- Refresh tokens are random opaque values and only their SHA-256 hashes are
  persisted. A successful refresh rotates the token through an atomic,
  conditional database update; concurrent reuse cannot produce two valid
  replacements.
- The native client refreshes after `401` and retries the request once. This is
  not a proactive timer and does not make an access JWT revocable immediately.
- Logout revokes the submitted refresh token, but an already-issued access JWT
  remains valid until its expiry. This is a known limitation and must be
  considered when assessing a stolen JWT.
- The backend currently stores GitHub OAuth/PAT credentials for internal GitHub
  API calls. The former `/api/v1/auth/token` endpoint has been removed, so
  those credentials are no longer returned to the native client.
- SignalR may carry the access JWT in a WebSocket query parameter. Logs,
  reverse-proxy access logs and diagnostics must redact query strings and
  authorization headers. nginx API access logging is disabled and the native
  client no longer prints response bodies; application error/request logging
  still requires an operational review before production.

### If a token may have leaked 🚨

1. Do not paste it into a report. Describe the location and timestamp instead.
2. Revoke or rotate the affected GitHub OAuth credential/PAT in GitHub.
3. Change the Statefalse JWT secret if an access JWT or signing secret may have
   been exposed; this invalidates tokens after the service restarts.
4. Remove or restrict the affected account/session and preserve only sanitized
   logs needed for investigation.
5. Report the incident privately using the process above.
