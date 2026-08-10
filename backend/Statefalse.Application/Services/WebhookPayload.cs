using System.Text.Json;

namespace Statefalse.Application;

/// <summary>
/// Shared JSON field extraction for GitHub webhook payloads.
/// Removes the TryGetRepo/TryGetWorkflowName duplication across handlers.
/// </summary>
public static class WebhookPayload
{
    public static string? TryGetRepo(JsonElement payload)
    {
        if (payload.TryGetProperty("repository", out var repo) &&
            repo.TryGetProperty("full_name", out var name))
            return name.GetString();
        return null;
    }

    public static string GetRepoOrUnknown(JsonElement payload)
        => TryGetRepo(payload) ?? "unknown";

    public static string? TryGetWorkflowName(JsonElement payload)
    {
        if (payload.TryGetProperty("workflow_run", out var run) &&
            run.TryGetProperty("name", out var name))
            return name.GetString();
        return null;
    }
}
