# Statefalse user guide

Statefalse is a native macOS menu-bar app for keeping pull requests, GitHub Actions and local branches close to your work.

Statefalse is an independent, free and open-source project licensed under
GPLv3. Individuals, teams and organizations may use it subject to the terms of
the license. It is not affiliated with any employer, client, vendor or other
organization.

See the project [`LICENSE`](https://github.com/trashdb/statefalse/blob/main/LICENSE),
[`PRIVACY.md`](https://github.com/trashdb/statefalse/blob/main/PRIVACY.md) and
[`CONTRIBUTING.md`](https://github.com/trashdb/statefalse/blob/main/CONTRIBUTING.md)
for more information. Administrators should also read the
[`ADMIN-GUIDE.md`](ADMIN-GUIDE.md) and [`WEBHOOKS.md`](WEBHOOKS.md).

## Availability

- **macOS:** available now.
- **Windows:** no release is currently available.

Statefalse currently requires **macOS Sequoia or newer**.

## Install Statefalse

1. Download the latest [macOS release](https://github.com/trashdb/statefalse/releases/latest).
2. Download the ZIP and its `SHA256SUMS` file.
3. Verify the checksum before opening the app.
4. Extract `Statefalse.app` into `~/Applications` or `/Applications`.
5. Open the app. Its icon appears in the macOS menu bar.

The current release is distributed directly and is not currently notarized. macOS may therefore ask you to confirm that you want to open the app. Only bypass quarantine after verifying the checksum and only when the release came from the Statefalse project repository.

If macOS still reports that the app cannot be opened, and the checksum is correct, remove the quarantine attribute from the verified app bundle before opening it:

```bash
xattr -dr com.apple.quarantine Statefalse.app
```

You can then move the app to `~/Applications` or `/Applications` and open it again. Do not remove quarantine from an app downloaded from an unknown or untrusted source.

## First-time setup: from download to notifications

Complete these steps in order after installing Statefalse:

1. **Sign in with GitHub.** Open the menu-bar icon and choose **Sign in with GitHub**, then authorize the requested permissions in your browser.
2. **Configure a PAT when needed.** Open **Settings → Personal Access Token**, paste a token with the minimum permissions required by your repositories, and save it. A PAT is needed for actions such as creating or merging pull requests, rerunning workflows, updating branches and changing draft/ready status.
3. **Choose the workspace.** In **Settings → Workspace Path**, select the directory containing the local Git repositories that Statefalse should scan.
4. **Configure Jira if you use it.** In **Settings → Jira Board URL**, enter the base URL used to build links from ticket references in branch names, for example `https://your-domain.atlassian.net/browse/`.
5. **Configure a webhook in every repository you want to monitor.** A repository administrator must add the GitHub webhook described in [Webhook setup](#webhook-setup). Without this webhook, Statefalse will not receive that repository's push events, so real-time updates and related notifications will not arrive.
6. **Keep Statefalse running.** The app stays in the macOS menu bar. Webhook events are delivered through the hosted API and then to the app's live connection; polling provides a slower fallback when a live connection or event is unavailable.

After setup, run or update a workflow, or open a pull request, to confirm that the repository appears in the app and that its status changes are reflected in **Workflow History**, the pull-request cards and macOS notifications.

## Connect GitHub

1. Click the Statefalse icon in the menu bar.
2. Choose **Sign in with GitHub**.
3. Review and authorize the requested GitHub permissions.
4. Return to Statefalse when the connection completes.

Statefalse uses GitHub OAuth for sign-in. The session used by the app is separate from your GitHub access token and is stored locally in the macOS Keychain when you choose to stay signed in. The public hosted backend also processes the metadata and credentials required for its GitHub integration; review [`PRIVACY.md`](https://github.com/trashdb/statefalse/blob/main/PRIVACY.md) before connecting company or client repositories.

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

For the hosted service, notifications depend on three parts working together: GitHub sends an event to the repository webhook, the Statefalse API validates and processes it, and the app receives the update over its live connection. If the webhook is missing or disabled, the app cannot receive push events for that repository. The app may discover some state through polling, but polling is not a replacement for configuring the webhook.

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

## Webhook setup

This setup is required for each repository whose GitHub events should reach Statefalse. You need repository administrator permission to add it.

For the public hosted service, use the detailed [webhook guide](WEBHOOKS.md). The short version is:

1. Open the repository on GitHub and go to **Settings → Webhooks → Add webhook**.
2. Set **Payload URL** to `https://api.statefalse.com/api/webhook/github`.
3. Set **Content type** to `application/json`.
4. Select **Individual events** and enable **Workflow runs**, **Pull requests**, **Pull request reviews**, **Issue comments**, **Pull request review comments** and **Check suites**.
5. In **Secret**, enter the secret configured for the Statefalse API. For a self-hosted instance, use the value of its `WebhookSecret`; for the public service, request the matching value from the service operator. An arbitrary or empty secret will cause the API to reject deliveries.
6. Leave the webhook **Active** and save it.
7. Use the webhook's **Recent Deliveries** tab in GitHub to check that deliveries succeed. A failed delivery will prevent the corresponding update from reaching the app.

Self-hosted instances must use their own API URL and webhook configuration. Do not copy the public URL when using a different backend.

## Privacy and security

- Sign-in uses GitHub OAuth.
- The app communicates with the Statefalse API over HTTPS.
- GitHub webhook events are verified using a signed HMAC-SHA256 signature before processing.
- The app stores its local session in the macOS Keychain when persistence is enabled.
- Local branch inspection is performed on your Mac.
- Statefalse does not claim to replace GitHub as the source of truth.

For the project security policy and responsible disclosure information, see [`SECURITY.md`](https://github.com/trashdb/statefalse/blob/main/SECURITY.md). Statefalse is an independent project and does not replace your security or compliance review.

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
