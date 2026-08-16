using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Statefalse.Application;

/// <summary>
/// AI provider orchestration: OpenAI-compatible / Copilot / Anthropic
/// chat-completions. Single responsibility: turn a prompt into provider text.
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
        var provider = (request.Provider ?? "openai").ToLower();
        var model = request.Model;

        return provider switch
        {
            "anthropic" => await CallAnthropicAsync(request, model ?? "claude-sonnet-4-20250514"),
            _ => await CallOpenAICompatibleAsync(request, provider, model ?? "gpt-4o")
        };
    }

    private async Task<string?> CallOpenAICompatibleAsync(AiRequest request, string provider, string model)
    {
        var messages = new[]
        {
            new { role = "system", content = request.SystemPrompt },
            new { role = "user", content = request.UserPrompt }
        };

        string? token = null;
        string baseUrl;

        if (!string.IsNullOrEmpty(request.ApiKey))
        {
            token = request.ApiKey;
            baseUrl = provider == "copilot" ? "https://api.githubcopilot.com" : "https://api.openai.com/v1";
        }
        else if (!string.IsNullOrEmpty(request.OAuthToken))
        {
            token = request.OAuthToken;
            baseUrl = "https://api.githubcopilot.com";
        }
        else
        {
            return null;
        }

        var body = new
        {
            messages,
            model,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature
        };

        var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        req.Headers.UserAgent.ParseAdd("Statefalse");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
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

    private async Task<string?> CallAnthropicAsync(AiRequest request, string model)
    {
        if (string.IsNullOrEmpty(request.ApiKey)) return null;

        var body = new
        {
            model,
            max_tokens = request.MaxTokens,
            temperature = request.Temperature,
            system = request.SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = request.UserPrompt }
            }
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        req.Headers.UserAgent.ParseAdd("Statefalse");
        req.Headers.Add("x-api-key", request.ApiKey);
        req.Headers.Add("anthropic-version", "2023-06-01");
        req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        return await SendAsync(req, response =>
        {
            if (response.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    if (item.TryGetProperty("type", out var type) && type.GetString() == "text" &&
                        item.TryGetProperty("text", out var text))
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
