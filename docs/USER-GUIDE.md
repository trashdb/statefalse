# Statefalse user guide

Statefalse is a native macOS menu-bar app for keeping pull requests, GitHub Actions and local branches close to your work.

## Availability

- **macOS:** available now.
- **Windows:** coming soon.

Statefalse currently requires **macOS Sequoia or newer**.

## Install Statefalse

1. Download the latest release from [GitHub Releases](https://github.com/trashdb/statefalse/releases/latest).
2. Download the ZIP and its `SHA256SUMS` file.
3. Verify the checksum before opening the app.
4. Extract `Statefalse.app` into `~/Applications` or `/Applications`.
5. Open the app. Its icon appears in the macOS menu bar.

The current release is distributed directly and is not currently notarized. macOS may therefore ask you to confirm that you want to open the app. Only bypass quarantine after verifying the checksum and only when the release came from the official Statefalse repository.

## Connect GitHub

1. Click the Statefalse icon in the menu bar.
2. Choose **Sign in with GitHub**.
3. Review and authorize the requested GitHub permissions.
4. Return to Statefalse when the connection completes.

Statefalse uses GitHub OAuth for sign-in. The session used by the app is separate from your GitHub access token and is stored locally in the macOS Keychain when you choose to stay signed in.

## Personal Access Token

Some GitHub actions may require a personal access token, including:

- Creating a pull request.
- Rerunning workflows.
- Updating a branch.
- Changing draft/ready status.
- Merging a pull request.

Add it from **Settings → Personal Access Token**. Use the minimum permissions required for the repositories you work with, follow your organization's SSO requirements and never share the token in screenshots, issues or chat.

## What you can do

### Pull requests

Statefalse shows pull requests that are relevant to you, including their repository, branches, review state, CI state and whether they are ready to merge.

Open a pull request to inspect:

- Mergeability.
- Workflow and check status.
- Commits.
- Changed files.
- Ahead/behind information.
- Recent comments.
- Available actions.

### Status badges

| Status | Meaning |
|---|---|
| **DRAFT** | The pull request is still being prepared. |
| **WAITING** | Checks or other required information are still pending. |
| **REVIEW** | Human review is still needed. |
| **READY** | Checks and approval indicate that it can be merged. |
| **FAIL** | One or more relevant checks or workflows failed. |
| **MERGED** | The pull request was merged recently. |

The final merge decision remains with GitHub and your repository rules.

### Workflow notifications

Statefalse can notify you about important workflow changes, including failures and successful reruns. Notifications include the repository and run context and can take you directly back to GitHub.

The app also keeps workflow history so you can review recent runs without searching through several GitHub pages.

### Branches

Statefalse can discover local repositories and show local and remote branches. Depending on the repository and your permissions, you can:

- Switch branches.
- Create or delete local branches.
- Update a branch.
- Identify Jira-style ticket references.
- Create a pull request from the current branch.

Local Git actions run on your Mac. Statefalse does not upload your entire local repository to provide branch management.

### Conflict awareness

The app can watch local repositories for changes that may overlap with files recently merged into the main branch. When it detects a potential conflict, it gives you context so you can inspect the branch before the problem grows.

## Settings

| Setting | Purpose |
|---|---|
| **Workspace Path** | Directory Statefalse scans for local Git repositories. |
| **Favorite Repo** | Repository used by quick links such as “See all PRs”. |
| **Jira Board URL** | Base URL used for detected ticket links. |
| **Personal Access Token** | Optional credential for GitHub actions that need it. |

## Privacy and security

- Sign-in uses GitHub OAuth.
- The app communicates with the Statefalse API over HTTPS.
- GitHub webhook events are verified using a signed HMAC-SHA256 signature before processing.
- The app stores its local session in the macOS Keychain when persistence is enabled.
- Local branch inspection is performed on your Mac.
- Statefalse does not claim to replace GitHub as the source of truth.

For the project security policy and responsible disclosure information, see [`SECURITY.md`](https://github.com/trashdb/statefalse/blob/main/SECURITY.md).

## Troubleshooting

### The app does not open

Verify the checksum of the downloaded release. If it is correct and macOS still blocks the unsigned package, use macOS's Open action from Finder and confirm the prompt. Do not bypass security controls for a package from an unknown source.

### GitHub login does not complete

Restart the login flow and keep the browser open until it returns to Statefalse. The native callback is temporary and expires if it does not receive the response in time.

### Pull requests are not appearing

Check that:

1. You are signed in to the expected GitHub account.
2. The repository is accessible to that account.
3. The app has had time to synchronize.
4. The pull request is open or was merged recently.
5. The API is reachable.

### A workflow action fails

Check the workflow permissions in GitHub and whether a personal access token is required. Statefalse cannot bypass repository rules, branch protection or GitHub permissions.

### The app shows old information

Statefalse combines local cached data, webhook updates, polling and live GitHub queries. Open the relevant pull request to refresh its detail, or restart the app if the local session/network connection is stale.

## Source code and releases

- [Source code](https://github.com/trashdb/statefalse)
- [Latest releases](https://github.com/trashdb/statefalse/releases/latest)
- [Security policy](https://github.com/trashdb/statefalse/blob/main/SECURITY.md)
