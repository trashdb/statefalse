# Security hardening roadmap

This roadmap prioritizes security, reliability, and production readiness over new product features.

## 1. Resource-level authorization and isolation — completed 2026-08-29

Ensure every authenticated user can only read or mutate resources they own or are explicitly allowed to access. Cover PR detail/commits/files/checks, PR actions, subscriber enumeration, workflow targets, workflow reruns, notifications, and cross-user regression tests.

Implemented: PR reads are scoped to authors/subscribers; PR mutations are owner-only; subscriber enumeration is scoped; workflow target changes and reruns are owner-only; subscriber ID matching is delimiter-aware; and cross-user integration regressions cover the protected boundaries. Application-level multi-tenancy remains a separate future phase.

The current deployment model is one isolated instance/database per tenant. Application-level multi-tenancy (`TenantId`, organization membership, and tenant-scoped persistence) is a separate future step and is not assumed by this phase.

## 2. Encrypt GitHub credentials at rest — core completed 2026-08-29

Implemented and deployed: OAuth access tokens and user PATs are encrypted with AES-256-GCM using an external key, legacy plaintext rows are migrated idempotently at startup, credentials and refresh sessions can be revoked, the shared PAT fallback is disabled, and PostgreSQL backups are encrypted with a separate key. Remaining work: explicit key rotation, off-host backup replication and isolated restore testing, and GitHub scope review.

## 3. Harden webhooks

Add body-size limits, cancellation/timeouts, strict event validation, delivery deduplication, bounded HMAC processing, and regression tests for malformed, oversized, replayed, and incorrectly signed payloads.

## 4. Add PKCE to native OAuth

Bind the macOS localhost callback to a code verifier/challenge, preserve single-use state, and harden the callback listener and response handling.

## 5. Improve rate limiting and proxy trust

Use endpoint/user-aware limits and trust forwarded headers only from the configured reverse proxy.

## 6. Harden the systemd service — configuration completed 2026-09-01; VPS rollout pending

`statefalse.service` now runs under a dedicated `statefalse` account, releases are read-only to the API, logs use a dedicated writable directory, and the environment file is readable only by `root:statefalse`. Existing installations must run the setup/deployment migration and verify the unit, permissions and rollback on the VPS.

## 7. Make backups recoverable

Encrypt and replicate backups off-host, define retention/RPO/RTO, and test restoration automatically in an isolated environment.

## 8. Strengthen sessions and refresh-token rotation

Use short-lived access tokens, refresh-token families, reuse detection, and individual session revocation.

## 9. Improve logging and health checks — secret redaction completed 2026-09-01

Structured application log properties are now sanitized before reaching the console and file sinks: authorization and credential properties are replaced, and bearer tokens, OAuth parameters, HMAC headers, and cookies embedded in property text are redacted. Callers must continue using structured logging and must not interpolate secrets directly into message templates. Separate liveness/readiness checks with correct failure status codes remain part of this item.

## 10. Supply-chain and release hardening

Add SAST, secret scanning, SBOM generation, dependency monitoring, and signing/notarization verification for releases.
