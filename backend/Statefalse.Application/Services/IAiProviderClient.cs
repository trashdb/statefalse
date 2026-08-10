namespace Statefalse.Application;

/// <summary>
/// Chat-completion transport over OpenAI-compatible / Copilot / Anthropic / Gemini.
/// Returns null when the provider rejects the request or the response has no text.
/// </summary>
public interface IAiProviderClient
{
    Task<string?> CompleteAsync(AiRequest request);
}

public sealed record AiRequest(
    string SystemPrompt,
    string UserPrompt,
    string? ApiKey,
    string? Provider,
    string? Model,
    string? OAuthToken,
    int MaxTokens = 500,
    double Temperature = 0.3);
