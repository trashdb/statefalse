# Statefalse landing page

Static, dependency-free landing page for Statefalse. It intentionally lives separately from the ASP.NET backend and native macOS app.

## Run locally

From the repository root:

```bash
python3 -m http.server 8080 --directory landing
```

Open <http://localhost:8080>.

## Publish

The page is plain HTML/CSS with no build step. It can be deployed to GitHub Pages, Cloudflare Pages, Netlify or any static host with `landing/` as the publish directory.

For the Statefalse VPS, from the repository root run:

```bash
bash deploy/deploy-landing.sh user@vps-ip
```

The script publishes the files to the Statefalse document root mounted by the `underlayer-nginx` container, changes only the `statefalse.com` nginx vhost, validates nginx before reloading it, and leaves `api.statefalse.com` proxied to the backend.

The download buttons point directly to the next published `v0.2.12` ZIP asset:

`https://github.com/trashdb/statefalse/releases/download/v0.2.12/Statefalse-v0.2.12.zip`

Update the three CTA links when a new release becomes the public download. The release-notes link remains dynamic.

## Content boundaries

The page describes capabilities currently documented in the repository: macOS, GitHub Actions, pull requests, local branches, actionable notifications and optional Copilot summaries. It does not claim that Statefalse is notarized, distributed through the App Store, multi-tenant or backed by Stripe/licensing yet.
