# Statefalse privacy information

_Last updated: 2026-08-21_

Statefalse is an independent, free and open-source project. This document
explains the data handled by the public Statefalse service and the macOS app.
It is informational and is not legal advice.

## Who operates the service

The public demonstration service is operated by the Statefalse project and is
hosted on infrastructure selected by the project owner. Statefalse is an
independent project and is not affiliated with any employer, client, vendor or
other organization.

## Data processed

Depending on the features you use, Statefalse may process:

- GitHub account identifiers, username and avatar information;
- GitHub OAuth access information required to connect your account;
- a personal access token if you choose to configure one;
- repository, pull request, check suite, workflow and webhook metadata;
- notification and subscription state required to deliver product features;
- technical logs needed to operate, secure and troubleshoot the service.

Statefalse does not need to upload the contents of your entire local Git
repositories for local branch inspection. Local Git operations are performed
by the macOS app on your Mac.

## How data is used

Data is used only to:

- authenticate you with GitHub;
- display pull requests, checks and workflow information;
- perform actions that you explicitly request;
- deliver notifications and synchronize webhook events;
- maintain the service, prevent abuse and diagnose failures.

Statefalse does not sell personal data and does not use repository metadata for
advertising.

## Credentials

Statefalse processes the credentials required to authenticate with GitHub and
perform actions that you explicitly request. These credentials are used for
the GitHub integration described in this document. Do not connect accounts or
repositories that your organization's security policy does not allow you to
use with an independently operated service.

Never include access tokens, passwords, private repository contents or other
secrets in issues, pull requests, screenshots or support messages.

## Retention and deletion

Account, repository metadata, workflow events and notification state may be
retained while they are needed to provide the service. Operational database
backups are retained for a limited period according to the deployment backup
policy. Removing a local app installation does not automatically delete data
already stored by the hosted backend.

To request deletion or ask a question about data handled by the public service,
open a private contact request through the repository owner's GitHub profile.
Do not include secrets or private repository data in the request.

## Third-party services

Statefalse integrates with GitHub. GitHub's own terms and privacy policy apply
to data processed by GitHub. The public web and download pages are hosted
separately from the API and may use GitHub Releases to distribute application
packages.

## Self-hosting

You may run your own instance under the terms of the GPLv3 license. A
self-hosted deployment has its own operator, storage, logging and retention
responsibilities. Operators should replace the example configuration, protect
secrets, restrict access and document their own privacy practices.

## Changes

This document may be updated when the service or its data handling changes.
The date above identifies the latest revision.

