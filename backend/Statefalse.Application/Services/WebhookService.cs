using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Statefalse.Application;

/// <summary>
/// GitHub webhook entry point: HMAC signature verification, payload parsing and
/// dispatch to the matching <see cref="IWebhookHandler"/> by X-GitHub-Event.
/// </summary>
public class WebhookService
{
    private readonly Dictionary<string, IWebhookHandler> _handlers;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookService> _logger;

    public WebhookService(
        IEnumerable<IWebhookHandler> handlers,
        IConfiguration configuration,
        ILogger<WebhookService> logger)
    {
        _handlers = handlers.ToDictionary(h => h.EventType, h => h, StringComparer.OrdinalIgnoreCase);
        _configuration = configuration;
        _logger = logger;
    }

    public List<WebhookLogEntry> GetLogs(int limit)
        => WebhookLog.GetRecent(limit);

    public async Task<ApiResult> HandleGitHubWebhookAsync(
        string? signatureHeader,
        Func<Task<string>> readRawBody,
        Func<Task<JsonElement?>> readJsonBody,
        string? eventType)
    {
        var webhookSecret = _configuration["WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret)
            || webhookSecret == "set-me-in-env-vars"
            || webhookSecret == "set-your-github-webhook-secret-here")
        {
            WebhookLog.Log("unknown", null, null, null, "rejected", "Webhook secret is not configured");
            return ApiResult.Unauthorized("Webhook secret is not configured");
        }

        if (string.IsNullOrEmpty(signatureHeader))
        {
            WebhookLog.Log("unknown", null, null, null, "rejected", "Missing X-Hub-Signature-256");
            return ApiResult.Unauthorized("Missing X-Hub-Signature-256");
        }

        var rawBody = await readRawBody();
        var key = Encoding.UTF8.GetBytes(webhookSecret);
        var hash = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(rawBody));
        var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signatureHeader),
                Encoding.UTF8.GetBytes(expected)))
        {
            WebhookLog.Log("unknown", null, null, null, "rejected", "Invalid webhook signature");
            return ApiResult.Unauthorized("Invalid signature");
        }

        var payload = await readJsonBody();
        if (payload is not { } body)
            return ApiResult.BadRequest("Invalid JSON payload");

        if (_handlers.TryGetValue(eventType ?? "", out var handler))
        {
            try
            {
                return await handler.HandleAsync(body);
            }
            catch (Exception ex)
            {
                // Log for visibility, then rethrow so GitHub retries the webhook.
                WebhookLog.Log(handler.EventType, null, WebhookPayload.TryGetRepo(body), null, "error", "Webhook handler failed");
                _logger.LogError(ex, "Webhook handler '{EventType}' failed", handler.EventType);
                throw;
            }
        }

        WebhookLog.Log(eventType ?? "unknown", null, WebhookPayload.TryGetRepo(body), null, "ignored", "Unsupported event type");
        return ApiResult.Ok($"Ignored: unsupported event '{eventType}'.");
    }
}
