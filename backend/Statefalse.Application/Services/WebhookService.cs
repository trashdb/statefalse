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
    private readonly IUnitOfWork _uow;

    public WebhookService(
        IEnumerable<IWebhookHandler> handlers,
        IConfiguration configuration,
        ILogger<WebhookService> logger,
        IUnitOfWork uow)
    {
        _handlers = handlers.ToDictionary(h => h.EventType, h => h, StringComparer.OrdinalIgnoreCase);
        _configuration = configuration;
        _logger = logger;
        _uow = uow;
    }

    public List<WebhookLogEntry> GetLogs(int limit)
        => WebhookLog.GetRecent(limit);

    public async Task<ApiResult> HandleGitHubWebhookAsync(
        string? signatureHeader,
        Func<Task<byte[]>> readRawBody,
        string? eventType,
        string? deliveryId,
        CancellationToken cancellationToken = default)
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

        if (string.IsNullOrWhiteSpace(deliveryId))
        {
            WebhookLog.Log(eventType ?? "unknown", null, null, null, "rejected", "Missing X-GitHub-Delivery");
            return ApiResult.BadRequest("Missing X-GitHub-Delivery");
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            WebhookLog.Log("unknown", null, null, null, "rejected", "Missing X-GitHub-Event");
            return ApiResult.BadRequest("Missing X-GitHub-Event");
        }

        var rawBody = await readRawBody();
        var key = Encoding.UTF8.GetBytes(webhookSecret);
        var hash = HMACSHA256.HashData(key, rawBody);
        var expected = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signatureHeader),
                Encoding.UTF8.GetBytes(expected)))
        {
            WebhookLog.Log("unknown", null, null, null, "rejected", "Invalid webhook signature");
            return ApiResult.Unauthorized("Invalid signature");
        }

        JsonElement body;
        try
        {
            body = JsonSerializer.Deserialize<JsonElement>(rawBody);
        }
        catch (JsonException)
        {
            return ApiResult.BadRequest("Invalid JSON payload");
        }

        if (!await _uow.TryClaimWebhookDeliveryAsync(deliveryId, eventType, cancellationToken))
            return ApiResult.Ok(new { duplicate = true, delivery_id = deliveryId });

        if (_handlers.TryGetValue(eventType, out var handler))
        {
            try
            {
                var result = await handler.HandleAsync(body);
                await _uow.CompleteWebhookDeliveryAsync(deliveryId, cancellationToken);
                return result;
            }
            catch (Exception ex)
            {
                await _uow.ReleaseWebhookDeliveryAsync(deliveryId, CancellationToken.None);
                // Log for visibility, then rethrow so GitHub retries the webhook.
                WebhookLog.Log(handler.EventType, null, WebhookPayload.TryGetRepo(body), null, "error", "Webhook handler failed");
                _logger.LogError(ex, "Webhook handler '{EventType}' failed", handler.EventType);
                throw;
            }
        }

        await _uow.CompleteWebhookDeliveryAsync(deliveryId, cancellationToken);
        WebhookLog.Log(eventType, null, WebhookPayload.TryGetRepo(body), null, "ignored", "Unsupported event type");
        return ApiResult.Ok($"Ignored: unsupported event '{eventType}'.");
    }
}
