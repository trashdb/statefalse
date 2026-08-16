using System.Text.RegularExpressions;
using Statefalse.Domain.Contracts;

namespace Statefalse.Application;

/// <summary>
/// PR preview assembly: PR template fetch, commit listing, Copilot summary
/// and suggested-body building. Uses <see cref="IAiProviderClient"/> for the
/// Copilot completion; all GitHub file access goes through <see cref="IGitHubClient"/>.
/// </summary>
public class PrPreviewService
{
    private readonly IGitHubClient _github;
    private readonly IAiProviderClient _ai;
    private readonly ILogger<PrPreviewService> _logger;

    private static readonly string[] TemplatePaths =
    {
        ".github/PULL_REQUEST_TEMPLATE.md",
        ".github/pull_request_template.md",
        ".github/pull_request_template.txt",
        "PULL_REQUEST_TEMPLATE.md",
        "pull_request_template.md",
        "docs/PULL_REQUEST_TEMPLATE.md",
        "docs/pull_request_template.md",
        ".github/PULL_REQUEST_TEMPLATE/template.md",
        ".github/PULL_REQUEST_TEMPLATE/default.md"
    };

    public PrPreviewService(IGitHubClient github, IAiProviderClient ai, ILogger<PrPreviewService> logger)
    {
        _github = github;
        _ai = ai;
        _logger = logger;
    }

    public async Task<PrPreviewResult> BuildPreviewAsync(string repo, string baseBranch, string head, string title, bool useAI, string? restToken, string? copilotToken)
    {
        string? template = null;
        foreach (var path in TemplatePaths)
        {
            template = await FetchFileContent(repo, path, restToken);
            if (template != null)
            {
                _logger.LogInformation("PrPreview: found template at {Path}", path);
                break;
            }
        }
        if (template == null)
        {
            _logger.LogWarning("PrPreview: no PR template found for repo={Repo}", repo);
        }

        var commits = await GetCommitsBetween(repo, baseBranch, head, restToken);
        _logger.LogInformation("PrPreview: fetched {Count} commits for {Base}...{Head}", commits.Count, baseBranch, head);

        var summary = "";
        string? summaryError = null;
        if (useAI && commits.Count > 0)
        {
            if (!string.IsNullOrEmpty(copilotToken))
            {
                _logger.LogInformation("PrPreview: calling Copilot API for summary (oauthToken present)");
                summary = await GenerateSummaryAsync(commits, copilotToken);
                if (string.IsNullOrEmpty(summary))
                {
                    summaryError = "Copilot API returned empty response. Token may be expired — re-login to GitHub.";
                    _logger.LogWarning("PrPreview: Copilot returned empty summary");
                }
                else
                    _logger.LogInformation("PrPreview: Copilot summary generated ({Len} chars)", summary.Length);
            }
            else
            {
                summaryError = "No OAuth token available. Login to GitHub to enable Copilot summaries.";
                _logger.LogWarning("PrPreview: no OAuth token for Copilot");
            }
        }

        var ticketMatch = Regex.Match(head, @"[A-Z]+-\d+");
        var ticketNumber = ticketMatch.Success ? ticketMatch.Value : "";
        var suggestedBody = BuildBody(template, ticketNumber, summary, commits);

        return new PrPreviewResult(template ?? "", commits, summary, suggestedBody, summaryError);
    }

    public async Task<string?> FetchFileContent(string repo, string path, string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        var resp = await _github.GetAsync($"/repos/{repo}/contents/{Uri.EscapeDataString(path)}", token);
        if (resp.StatusCode is < 200 or >= 300 || resp.Body is not { } doc) return null;

        if (doc.TryGetProperty("content", out var contentProp))
        {
            var base64 = contentProp.GetString() ?? "";
            var bytes = Convert.FromBase64String(base64.Trim());
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        return null;
    }

    public async Task<List<string>> GetCommitsBetween(string repo, string baseRef, string headRef, string? token)
    {
        if (string.IsNullOrEmpty(token)) return [];
        var encodedBase = Uri.EscapeDataString(baseRef);
        var encodedHead = Uri.EscapeDataString(headRef);
        var resp = await _github.GetAsync($"/repos/{repo}/compare/{encodedBase}...{encodedHead}", token);
        if (resp.StatusCode is < 200 or >= 300 || resp.Body is not { } doc) return [];

        var result = new List<string>();
        if (doc.TryGetProperty("commits", out var commitsProp))
        {
            foreach (var c in commitsProp.EnumerateArray())
            {
                var msg = c.GetProperty("commit").GetProperty("message").GetString() ?? "";
                result.Add(msg.Split('\n')[0]);
            }
        }
        return result;
    }

    private async Task<string> GenerateSummaryAsync(List<string> commits, string oauthToken)
    {
        var commitText = string.Join("\n", commits.Select(c => $"- {c}"));
        var prompt = $"Write a detailed PR description summary in English based on these commit messages. Include what was changed and why:\n\n{commitText}\n\nDetailed description:";

        var systemPrompt = "You are a senior developer writing clear, concise PR descriptions for a team codebase. Write in complete paragraphs, explain the context and reasoning behind changes.";
        return await _ai.CompleteAsync(new AiRequest(
            SystemPrompt: systemPrompt,
            UserPrompt: prompt,
            OAuthToken: oauthToken,
            MaxTokens: 1000,
            Temperature: 0.7)) ?? "";
    }

    private static string BuildBody(string? template, string ticketNumber, string summary, List<string> commits)
    {
        var body = template ?? "";

        // Strip boilerplate before "## 📝 Description"
        var descIdx = body.IndexOf("## 📝 Description", StringComparison.Ordinal);
        if (descIdx >= 0)
            body = body[descIdx..];
        else
        {
            // Fallback: remove common boilerplate lines
            var lines = body.Split('\n').Where(l =>
                !l.TrimStart().StartsWith("### **PR Title:**") &&
                !l.TrimStart().StartsWith("**Description:**")).ToList();
            body = string.Join("\n", lines);
        }

        if (!string.IsNullOrEmpty(ticketNumber))
        {
            body = body.Replace("[LOY-XXX]", $"[{ticketNumber}]")
                       .Replace("[LOY-000]", $"[{ticketNumber}]")
                       .Replace("[TICKET]", ticketNumber)
                       .Replace("{ticket}", ticketNumber)
                       .Replace("TICKET_NUMBER", ticketNumber);
        }

        if (!string.IsNullOrEmpty(summary))
        {
            body = body.Replace("What change does this PR introduce?", summary);
        }

        return body.Trim();
    }
}

public sealed record PrPreviewResult(
    string Template,
    List<string> Commits,
    string Summary,
    string SuggestedBody,
    string? SummaryError);
