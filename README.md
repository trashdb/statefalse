# statefalse

macOS menu bar app that tracks GitHub Actions workflows, notifies you on failures, lets you rerun workflows, assign targets, track open PRs with real‑time status, and manage local/remote git branches with checkout, delete, Jira integration, and direct PR creation from the app.

For the user-facing installation and feature guide, see [`docs/USER-GUIDE.md`](docs/USER-GUIDE.md).

## Project status and ownership

Statefalse is an independent, free and open-source project created in
personal time. It was not commissioned by a client and is not an official
product of Thoughtworks, easyJet, GitHub or any other organization.

Statefalse is available for individuals, teams and organizations to use,
including internal and commercial use, under the terms of the GPLv3 license.
It is not restricted to a particular employer or organization.

The public hosted instance is operated as a project demonstration. Teams
should review their own security, privacy and compliance requirements before
connecting company or client repositories.

See [`LICENSE`](LICENSE), [`PRIVACY.md`](PRIVACY.md),
[`CONTRIBUTING.md`](CONTRIBUTING.md) and [`SECURITY.md`](SECURITY.md).

## Requirements

- macOS Sequoia or newer
- Xcode (or CLI tools: `xcode-select --install`)
- A GitHub account with access to the repos you work on

## Install

### Install published release

Download a macOS release from [GitHub Releases](https://github.com/trashdb/statefalse/releases/latest). Current packages are unsigned and not notarized; verify `SHA256SUMS` before opening them.

Set `VERSION` to the release tag you want to install:

```bash
VERSION=vX.Y.Z
RELEASE_DIR="$HOME/Downloads/statefalse-$VERSION"
mkdir -p "$RELEASE_DIR"
cd "$RELEASE_DIR"
curl -fLO "https://github.com/trashdb/statefalse/releases/download/$VERSION/Statefalse-$VERSION.zip"
curl -fLO "https://github.com/trashdb/statefalse/releases/download/$VERSION/SHA256SUMS"
shasum -a 256 -c SHA256SUMS
```

Expected result:

```text
Statefalse-vX.Y.Z.zip: OK
```

To replace the previous installation, close the app, remove the old bundle and extract the verified release:

```bash
pkill -x Statefalse 2>/dev/null || true
sleep 1

rm -rf "$HOME/Applications/Statefalse.app"
mkdir -p "$HOME/Applications"

ditto -x -k "Statefalse-$VERSION.zip" .
```

Remove macOS download quarantine only after the checksum reports `OK`, then install and open the app:

```bash
xattr -dr com.apple.quarantine Statefalse.app
ditto Statefalse.app "$HOME/Applications/Statefalse.app"
rm -rf Statefalse.app
open "$HOME/Applications/Statefalse.app"
```

This quarantine step is required because current packages are unsigned and not notarized. Do not run it for packages from an untrusted source.

For future releases, the complete process can be run with the installer script. It verifies the checksum before removing the current app, cleans extracted releases, backups and local Xcode app products, then installs only `~/Applications/Statefalse.app`:

```bash
curl -fL https://raw.githubusercontent.com/trashdb/statefalse/main/native/install-release.sh \
  -o /tmp/statefalse-install-release.sh
bash /tmp/statefalse-install-release.sh vX.Y.Z
```

The script preserves downloaded ZIPs and checksums under `~/Downloads`. It does not remove a system-owned `/Applications/Statefalse.app`; if one remains, it prints a warning so it can be removed separately with administrator privileges.

### Build from source

`install.sh` is for development, not for installing a published release. It requires Xcode or the Xcode command-line tools, builds the project locally, copies it to `~/Applications/Statefalse.app`, and relaunches it. If the build fails, it can fall back to a previous local build.

```bash
git clone git@github.com:trashdb/statefalse.git
cd statefalse/native
bash install.sh
```

For day-to-day local development, use the Debug runner instead. It builds into
`native/.derived-data`, launches that app directly, and does not copy anything
to `~/Applications` or create a release:

```bash
cd statefalse/native
bash run-local.sh
```

You can also open `native/statefalse.xcodeproj` in Xcode and use **Product → Run**
with the `Statefalse` scheme. This is a macOS menu-bar app, so it runs locally
on macOS rather than in the iOS Simulator.

A 🔥 icon appears in your menu bar.

The install script builds the project, copies it to `~/Applications/Statefalse.app`, and relaunches. If the build fails it falls back to the previous build.

## First‑time setup

### 1. Sign in with GitHub

Click the 🔥 icon → **Sign in with GitHub**. Your browser opens to authorize the app (OAuth, scopes: `read:user,repo`). Once connected, you'll see your avatar and **Connected** in green.

### 2. Configure your Personal Access Token (PAT)

Some features (create PR, rerun workflows, update branch, draft/ready toggle) require a PAT:

1. Go to [github.com/settings/tokens](https://github.com/settings/tokens) → **Generate new token (classic)**
2. Choose the minimum scopes required by the repositories and actions you use.
3. If your organization enforces SAML SSO, authorize the token according to its policy.
4. Copy the token (starts with `github_pat_...`)
5. Open the app → ⚙️ **Settings** → **Personal Access Token** → paste and **Save**

### 3. Set your workspace path

The app scans a directory recursively to discover git repos for branch management.

Default: `~/Desktop/dev`. Change it in **Settings** → **Workspace Path** if your repositories live elsewhere.

### 4. Configure Jira (optional)

**Settings** → **Jira Board URL** — used when clicking a Jira ticket link in the branch detail popover.

Example: `https://your-domain.atlassian.net/browse/`

### 5. Select your favorite repo

**Settings** → **Favorite Repo** — pick the repository you work on most often. The **See All PRs** button opens the configured GitHub organization and repository.

## Interface

### Popover layout (top to bottom)

| Section | Height | Description |
|---------|--------|-------------|
| **Active PRs** | 170pt | PR cards with CI status + approval state |
| **Branches** | 180pt | Local/Remote git branches |
| **Running workflows** | auto | Current in‑progress runs with rerun/target buttons |
| **Toolbar** | auto | Settings, Workflow History, Webhook Log, See All PRs, avatar |

### PR status badges

| Badge | Meaning |
|-------|---------|
| **DRAFT** | PR is a draft — not ready for review |
| **WAITING** | Open, waiting for checks/review |
| **REVIEW** | Pending human approval (CI may be green) |
| **READY** | CI passed + approved — good to merge |
| **FAIL** | Checks are failing |
| **MERGED** | PR was merged (briefly shown before disappearing) |

Click a PR card to see a detail popover with mergeable state, CI badge per workflow, behind/ahead counts, latest comment preview, links to GitHub Compare/Checks, Convert to Draft / Mark as Ready, Update Branch, and Merge PR.

### Notifications

- **Failures:** red accent, loud sound, dock bounce
- **Approvals & comments:** blue accent, gentle sound
- **Conflict alerts:** blue accent, gentle sound — when someone merges files you're also touching
- Only for important workflows (CI, account‑api, lambdas, terraform) — CodeQL, Dependency Review, etc. are silently ignored.

### Workflow History

Recent workflow runs with success/failure/in-progress icons, rerun button, target assignment for completion notifications. Completed runs show duration; in‑progress runs show relative time.

### Branch management

#### Local Branches

Shows branches where you have commits. Click a branch for:
- Jira ticket link (auto‑detected from branch name)
- **Checkout** → runs `git checkout` + `git pull --rebase` (if upstream exists), then opens **JetBrains Rider**
- **Delete** → `git branch -D` (protected: `main`/`master` cannot be deleted)
- **Create PR** → editable preview with template + Copilot-generated summary from commit messages

#### Remote Branches

Lists branches from GitHub API. Merged branches can be deleted; unmerged are read‑only.

### Predictive Conflict Detection

The app watches your workspace repos in real time. When someone merges a PR to `main`, you get a notification if:
- You have **uncommitted changes** in the same files that were just merged
- Your **current branch** touches the same files (potential merge conflict)

Notifications arrive via SignalR (instant) with a 60‑second polling fallback. Deduplicated per file for 5 minutes.

### Create PR flow

1. Ticket number auto‑extracted from branch name → optionally prepended to the PR title
2. PR template loaded from `.github/pull_request_template.md`
3. Backend calls Copilot API to generate a summary from commit messages
4. Edit title and body before confirming
5. PR is created via GitHub API

## For repo admins: webhook setup

The public API base URL is `https://api.statefalse.com`. Self-hosted instances
must use their own API URL and webhook configuration.

Each repo needs a webhook pointing at the backend:

1. **Repo Settings → Webhooks → Add webhook**
2. **Payload URL:** `https://api.statefalse.com/api/webhook/github`
3. **Content type:** `application/json`
4. **Events:** ☑ Workflow runs, ☑ Pull requests, ☑ Pull request reviews, ☑ Issue comments, ☑ Pull request review comments, ☑ Check suites
5. **Secret:** enter the secret configured for the Statefalse API. For a self-hosted instance, use its `WebhookSecret`; for the public service, request the matching value from the service operator.
6. **Active:** ✅

## Settings

| Setting | Description |
|---------|-------------|
| **Workspace Path** | Directory to scan for git repos (default `~/Desktop/dev`) |
| **Jira Board URL** | Base URL for Jira ticket links |
| **Favorite Repo** | Used by "See All PRs" button |
| **Personal Access Token** | Per‑user PAT for API access |

## Architecture

The public guide covers the product behavior and setup. Deeper architecture and security notes are maintained separately as local-only documentation for the project owner.
