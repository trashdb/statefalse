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

The script publishes the files to the Statefalse document root on the
production server, changes only the `statefalse.com` nginx vhost, validates
nginx before reloading it, and leaves `api.statefalse.com` proxied to the
backend.

The download buttons point directly to the currently published release ZIP
asset. When publishing a new native release, update the version in
`landing/index.html` before deploying the landing:

`https://github.com/trashdb/statefalse/releases/download/vX.Y.Z/Statefalse-vX.Y.Z.zip`

Update the three CTA links when a new release becomes the public download. The
release-notes link remains dynamic. Keep the **Install guide** and **Webhooks**
links in the page pointing to `docs/USER-GUIDE.md` and `docs/WEBHOOKS.md`.

## Content boundaries

The page describes capabilities currently documented in the repository: macOS,
GitHub Actions, pull requests, local branches and actionable notifications. It
does not claim that Statefalse is notarized, distributed through the App Store,
multi-tenant or backed by Stripe/licensing. The project is free, open source
under GPLv3 and independent; it is not affiliated with any employer, client,
vendor or other organization.
