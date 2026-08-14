# Release process

## Versioning

Use Git tags such as `v0.2.0`. Each release records commit SHA, backend build, native build and migration state.

## Checklist

- [ ] PR approved and required CI green.
- [ ] Security and migration impact reviewed.
- [ ] Backup verified before schema change.
- [ ] Backend and Swift tests pass.
- [ ] Staging smoke tests pass.
- [ ] Rollback target identified.
- [ ] Production approval granted.
- [ ] Health, OAuth, webhook and SignalR validated after deploy.

## Database compatibility

Migrations are forward-only until tested recovery exists. Binary rollback is not database rollback. New schema changes must remain compatible with currently deployed binary during rollout.

## Artifacts

Build artifacts are immutable and accompanied by SHA-256 checksums. Never publish secrets, databases, `.env` files or local Xcode data. Production deploy requires explicit approval; push must not silently become production release.

## Native macOS release

Create a SemVer tag such as `v0.2.0` and push it. `.github/workflows/release.yml` runs native tests, builds an isolated Release app, verifies bundle metadata, and publishes these GitHub Release assets:

- `Statefalse-vX.Y.Z.zip`
- `SHA256SUMS`
- `release-manifest.txt`

Latest release: <https://github.com/trashdb/statefalse/releases/latest>. Users should download ZIP and checksum from GitHub Releases, not Actions artifacts.

Workflow can also be dispatched manually with an existing tag. Current package is unsigned and not notarized; users may need to verify the checksum and remove the download quarantine before opening it. Do not describe package as App Store or notarized distribution until Developer ID signing and notarization are enabled.
