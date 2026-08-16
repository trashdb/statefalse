namespace Statefalse.Application;

/// <summary>
/// Chat-completion transport over GitHub Copilot.
/// Returns null when the provider rejects the request or the response has no text.
/// </summary>
public interface IAiProviderClient
{
    Task<string?> CompleteAsync(AiRequest request);
}

public sealed record AiRequest(
    string SystemPrompt,
    string UserPrompt,
    string? OAuthToken,
    int MaxTokens = 500,
    double Temperature = 0.3);
