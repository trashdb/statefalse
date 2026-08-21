# GitHub webhook setup

Statefalse uses GitHub webhooks to deliver pull-request, workflow and review changes without waiting for a polling cycle. Configure one webhook for each repository you want to monitor.

## Hosted service

- **Payload URL:** `https://api.statefalse.com/api/webhook/github`
- **Content type:** `application/json`
- **Secret:** use the secret supplied for the hosted Statefalse service
- **Active:** enabled

In GitHub, open **Repository → Settings → Webhooks → Add webhook**, select **Individual events**, and enable:

- Workflow runs
- Pull requests
- Pull request reviews
- Issue comments
- Pull request review comments
- Check suites

Save the webhook and use **Recent Deliveries** to send a test delivery. A successful delivery should receive a 2xx response from the API.

## Self-hosted service

Use the public URL only for the hosted service. For a self-hosted deployment, replace the payload URL with:

```text
https://your-api.example.com/api/webhook/github
```

Set the same random secret in the backend configuration and GitHub. The backend setting is `WebhookSecret`; generate a value with:

```bash
openssl rand -hex 32
```

Store it in a secret manager or a protected environment file. Never commit it to the repository. An empty, outdated or mismatched secret causes HMAC validation to fail and the delivery is rejected.

## Troubleshooting deliveries

1. Confirm that the webhook is **Active**.
2. Confirm the payload URL points to the correct API, including `/api/webhook/github`.
3. Confirm **Content type** is `application/json`.
4. Confirm the GitHub secret exactly matches `WebhookSecret`.
5. Check **Recent Deliveries** for the HTTP status and response body.
6. Check the backend logs without exposing request bodies, tokens or signatures.

Polling may discover some later state, but it is not a substitute for webhooks when you need real-time notifications.

