<div align="center">
  <img src="native/Assets.xcassets/AppIcon.appiconset/icon_1024.png" alt="Statefalse icon" width="128">
  <h1>Statefalse</h1>
  <p><strong>Your GitHub pull requests, workflows and local branches — under control.</strong></p>
  <p>
    <a href="https://github.com/trashdb/statefalse">⭐ Star us on GitHub</a>
    ·
    <a href="docs/USER-GUIDE.md">Install and configureee</a>
    ·
    <a href="https://github.com/trashdb/statefalse/releases/latest">Download the latest release</a>
  </p>
</div>

Statefalse is an independent, open-source macOS menu-bar app for GitHub pull requests, Actions workflows, notifications and local Git branches.

[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![C#](https://img.shields.io/badge/C%23-68217A?logo=csharp&logoColor=white)](https://learn.microsoft.com/dotnet/csharp/)
[![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/aspnet/core/)
[![Entity Framework Core](https://img.shields.io/badge/EF_Core-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/ef/core/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![SignalR](https://img.shields.io/badge/SignalR-512BD4?logo=dotnet&logoColor=white)](https://learn.microsoft.com/aspnet/core/signalr/introduction)
[![Swift 6](https://img.shields.io/badge/Swift-6-F05138?logo=swift&logoColor=white)](https://www.swift.org/)
[![SwiftUI](https://img.shields.io/badge/SwiftUI-007AFF?logo=swift&logoColor=white)](https://developer.apple.com/xcode/swiftui/)
[![macOS](https://img.shields.io/badge/macOS-000000?logo=apple&logoColor=white)](https://www.apple.com/macos/)
[![HTML5](https://img.shields.io/badge/HTML5-E34F26?logo=html5&logoColor=white)](https://developer.mozilla.org/docs/Web/HTML)
[![CSS3](https://img.shields.io/badge/CSS3-1572B6?logo=css3&logoColor=white)](https://developer.mozilla.org/docs/Web/CSS)

## Contents

- [Start here](#start-here)
- [Install](#install)
  - [Requirements](#requirements)
  - [Published release](#published-release-recommended)
  - [Build from source](#build-from-source)
- [Configure](#configure)
- [Webhooks](#webhooks)
- [What Statefalse does](#what-statefalse-does)


## Start here

- [Install and configure Statefalse](docs/USER-GUIDE.md) — complete user guide.
- [Install from a release](docs/USER-GUIDE.md#install-statefalse) — checksum verification and macOS first launch.
- [Configure OAuth, PAT and Jira](docs/USER-GUIDE.md#first-time-setup-from-download-to-notifications).
- [Configure local Git authentication](docs/GIT-LOCAL.md) — SSH or HTTPS with the macOS Keychain.
- [Configure GitHub webhooks](docs/WEBHOOKS.md) — required events, HMAC secret and delivery checks.
- [Configure a self-hosted backend](docs/ADMIN-GUIDE.md).
- [Security policy](SECURITY.md) · [Privacy](PRIVACY.md) · [Contributing](CONTRIBUTING.md) · [License](LICENSE).

## Install

### Requirements

- macOS Sequoia or newer.
- A GitHub account with access to the repositories you want to use.
- Xcode or the Xcode command-line tools only when building from source.

### Published release (recommended)

Download the [latest macOS release](https://github.com/trashdb/statefalse/releases/latest), including its `SHA256SUMS` file. Verify the checksum before opening the app, then extract `Statefalse.app` into `~/Applications` or `/Applications`.

For a terminal-based install, replace `vX.Y.Z` with the release tag:

```bash
VERSION=vX.Y.Z
RELEASE_DIR="$HOME/Downloads/statefalse-$VERSION"
mkdir -p "$RELEASE_DIR" && cd "$RELEASE_DIR"
curl -fLO "https://github.com/trashdb/statefalse/releases/download/$VERSION/Statefalse-$VERSION.zip"
curl -fLO "https://github.com/trashdb/statefalse/releases/download/$VERSION/SHA256SUMS"
shasum -a 256 -c SHA256SUMS
ditto -x -k "Statefalse-$VERSION.zip" .
mkdir -p "$HOME/Applications"
ditto Statefalse.app "$HOME/Applications/Statefalse.app"
open "$HOME/Applications/Statefalse.app"
```

The releases are currently unsigned and not notarized. If macOS blocks a verified release, use Finder's **Open** action first. Only as a last resort, and only after a successful checksum verification, remove quarantine with `xattr -dr com.apple.quarantine Statefalse.app`.

The [release installer](native/install-release.sh) automates checksum verification and installation. Do not use quarantine-removal commands for downloads from an untrusted source.

### Build from source

```bash
git clone https://github.com/trashdb/statefalse.git
cd statefalse/native
bash install.sh
```

For development without copying the app to `~/Applications`, run `bash run-local.sh` or open `native/statefalse.xcodeproj` in Xcode.

## Configure

After installation:

1. Open the Statefalse wave mark in the macOS menu bar and choose **Sign in with GitHub**.
2. Open **Settings → Personal Access Token** only if you need actions such as creating/merging pull requests, rerunning workflows, updating branches or changing draft status. Use the minimum permissions required.
3. Set **Workspace Path** to the directory containing local repositories.
4. Optionally set **Jira Board URL**, for example `https://your-domain.atlassian.net/browse/`.
5. Set **Favorite Repo** for quick links to pull requests.
6. Ask a repository administrator to configure the webhook described in [Webhook setup](docs/WEBHOOKS.md).

OAuth signs you in; a PAT is a separate backend credential for actions that require additional GitHub permissions. Local `pull`, `fetch` and `push` use your Mac's SSH configuration or Git credential helper. Never paste either credential into issues, screenshots or support messages.

## Webhooks

For the hosted service, use `https://api.statefalse.com/api/webhook/github`. For self-hosting, use the URL of your own API. Configure `application/json`, the events listed in the [webhook guide](docs/WEBHOOKS.md), the matching `WebhookSecret`, and leave the webhook active. Polling can provide a slower fallback, but it does not replace webhook configuration for real-time updates.

## What Statefalse does

- Shows pull requests, reviews, checks and workflow status.
- Sends actionable notifications for failures, approvals, comments and possible local conflicts.
- Lets you inspect local and remote branches and perform permitted GitHub actions.
- Keeps local branch inspection on your Mac; GitHub remains the source of truth.

Statefalse is not affiliated with any employer, client, vendor or other organization. It is provided under GPLv3; review the project policies before connecting repositories.
