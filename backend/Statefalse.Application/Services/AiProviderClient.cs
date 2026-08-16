using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Statefalse.Application;

/// <summary>
/// GitHub Copilot chat-completion transport. Single responsibility: turn a prompt
/// into provider text when the user has an OAuth token.
/// </summary>
public sealed class AiProviderClient : IAiProviderClient
{
    private readonly HttpClient _client;
    private readonly ILogger<AiProviderClient> _logger;

    public AiProviderClient(ILogger<AiProviderClient> logger, HttpMessageHandler? handler = null)
    {
        _logger = logger;
        _client = handler != null
            ? new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) }
            : new HttpClient() { Timeout = TimeSpan.FromSeconds(30) };
    }

    public async Task<string?> CompleteAsync(AiRequest request)
    {
        return await CallCopilotAsync(request);
    }

    private async Task<string?> CallCopilotAsync(AiRequest request)
    {
        if (string.IsNullOrEmpty(request.OAuthToken)) return null;

        var messages = new[]
        {
            new { role = "system", content = request.SystemPrompt },
            new { role = "user", content = request.UserPrompt }
        };

        var body = new
        {
            messages,
            model = "gpt-4o",
            max_tokens = request.MaxTokens,
            temperature = request.Temperature
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.githubcopilot.com/chat/completions");
        req.Headers.UserAgent.ParseAdd("Statefalse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.OAuthToken);
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        return await SendAsync(req, response =>
        {
            if (response.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
            {
                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var text))
                        return text.GetString();
                }
            }
            return null;
        });
    }

    private async Task<string?> SendAsync(HttpRequestMessage req, Func<JsonElement, string?> extract)
    {
        try
        {
            var resp = await _client.SendAsync(req);
            var content = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI provider error: status={Status}", (int)resp.StatusCode);
                return null;
            }

            using var doc = JsonDocument.Parse(content);
            return extract(doc.RootElement);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "AI provider call failed");
            return null;
        }
    }
}
