using System.Text.Json;
using Statefalse.Domain.Contracts;

namespace Statefalse.Application;

/// <summary>
/// Natural-language intent parsing for the ⌘K command palette. Builds the prompt
/// and parses the structured JSON response from <see cref="IAiProviderClient"/>.
/// </summary>
public class QueryInterpretationService
{
    private readonly IAiProviderClient _ai;
    private readonly ILogger<QueryInterpretationService> _logger;

    public QueryInterpretationService(IAiProviderClient ai, ILogger<QueryInterpretationService> logger)
    {
        _ai = ai;
        _logger = logger;
    }

    public async Task<object> InterpretAsync(InterpretRequest request, string? oauthToken)
    {
        var userPrompt = $@"The user typed this natural language query in a developer tool command palette: ""{request.Query}""

Interpret their intent and respond with a JSON object containing:
- ""action"": one of ""createPR"", ""openJiraTicket"", ""openJiraBoard"", ""openRepo"", ""checkoutBranch"", ""openPRs"", ""openSettings"", ""resync"", ""workflowHistory"", ""webhookLog"", ""unknown""
- ""message"": a short confirmation message in Spanish like ""Creando PR desde la rama actual…""
- ""params"": any relevant parameters (repo, branch, ticket number, etc.)

If you cannot determine the action, respond with action ""unknown"" and suggest what the user could try instead.
Only respond with the JSON object, no other text.";

        var systemPrompt = "You are a helpful assistant integrated into a developer tool. Interpret natural language queries and return structured JSON actions.";
        var reply = await _ai.CompleteAsync(new AiRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: userPrompt,
            ApiKey: request.ApiKey,
            Provider: request.AiProvider,
            Model: request.Model,
            OAuthToken: oauthToken,
            MaxTokens: 500,
            Temperature: 0.3));

        if (string.IsNullOrEmpty(reply))
            return new { action = "unknown", message = "Could not interpret query. AI service unavailable." };

        try
        {
            var parsed = JsonSerializer.Deserialize<InterpretResponse>(reply);
            return parsed ?? new InterpretResponse { Action = "unknown", Message = reply };
        }
        catch
        {
            return new InterpretResponse { Action = "unknown", Message = reply };
        }
    }
}
