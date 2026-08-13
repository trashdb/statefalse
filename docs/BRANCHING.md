# Branching and protection

- `main`: production, protected, PR only.
- `develop`: staging integration after staging infrastructure exists.
- `feature/*`: short-lived changes.
- `hotfix/*`: urgent fixes with post-incident review.
- `vX.Y.Z`: approved releases.

GitHub settings must require PR review, conversation resolution, required backend/native CI checks, CODEOWNERS review, stale approval dismissal and force-push prohibition. Configure production approval through GitHub Environment; repository files document policy but cannot enable branch protection themselves.
