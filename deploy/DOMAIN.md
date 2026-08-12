# Statefalse domain activation

Canonical hosts:

- `https://statefalse.com` — homepage; redirects to API until landing exists.
- `https://api.statefalse.com` — REST, SignalR, OAuth callback, GitHub webhooks.

## Cloudflare DNS

| Type | Name | Target | Proxy | TTL |
|---|---|---|---|---|
| A | `@` | `49.13.88.205` | Proxied | Auto |
| A | `api` | `49.13.88.205` | Proxied | Auto |
| CNAME | `www` | `statefalse.com` | Proxied | Auto |

Delete conflicting records for `@`, `api`, `www`. Do not add wildcard yet. Do not expose port `5000`. Set Cloudflare SSL/TLS to **Full (strict)** after certificate issuance; never Flexible.

## VPS activation

Run after DNS resolves:

```bash
ssh underlayer 'dig +short statefalse.com A; dig +short api.statefalse.com A'
scp deploy/nginx/statefalse-api.init.conf underlayer:/tmp/statefalse-api.init.conf
ssh underlayer 'sudo cp /tmp/statefalse-api.init.conf /opt/underlayer/core/docker/nginx/conf.d/30-statefalse-api.init.conf && docker exec underlayer-nginx nginx -t && docker exec underlayer-nginx nginx -s reload'
ssh underlayer 'docker exec underlayer-certbot certbot certonly --webroot --webroot-path /var/www/certbot --cert-name statefalse.com --keep-until-expiring -d statefalse.com -d www.statefalse.com -d api.statefalse.com'
scp deploy/nginx/statefalse-api.conf underlayer:/tmp/statefalse-api.conf
ssh underlayer 'sudo rm -f /opt/underlayer/core/docker/nginx/conf.d/30-statefalse-api.init.conf && sudo cp /tmp/statefalse-api.conf /opt/underlayer/core/docker/nginx/conf.d/30-statefalse-api.conf && docker exec underlayer-nginx nginx -t && docker exec underlayer-nginx nginx -s reload'
./deploy.sh underlayer
```

Validate:

```bash
curl -fsS https://api.statefalse.com/health
curl -fsS -o /dev/null -w '%{http_code}\n' https://api.statefalse.com/api/auth/login
```

GitHub OAuth App:

- Homepage: `https://statefalse.com`
- Callback: `https://api.statefalse.com/api/auth/callback`

Every GitHub repository webhook:

- Payload URL: `https://api.statefalse.com/api/webhook/github`
- Content type: `application/json`
- Keep existing secret/events.

After OAuth, webhook, REST, and SignalR validation, retire tunnel:

```bash
ssh underlayer 'sudo systemctl disable --now statefalse-tunnel'
```

Keep `deploy/statefalse-tunnel.service` for rollback until migration stable.
