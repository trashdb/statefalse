using System.Text;
using System.Text.Json;
using Statefalse.Application;
using Microsoft.Extensions.Logging.Abstractions;

namespace Statefalse.Api.Tests;

public class PrPreviewServiceTests
{
    private readonly FakeGitHubClient _github = new();
    private readonly FakeAiClient _ai = new();

    private PrPreviewService CreateService()
        => new(_github, _ai, NullLogger<PrPreviewService>.Instance);

    private static GitHubResponse JsonResponse(int status, object body)
        => new(status, JsonSerializer.SerializeToElement(body));

    private static string Base64(string s)
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

    private void StubTemplate(string content)
    {
        var path = Uri.EscapeDataString(".github/PULL_REQUEST_TEMPLATE.md");
        _github.Responses[$"/repos/acme/repo/contents/{path}"] = JsonResponse(200, new
        {
            content = Base64(content)
        });
    }

    private void StubCommits(params string[] messages)
        => _github.Responses[
                $"/repos/acme/repo/compare/{Uri.EscapeDataString("main")}...{Uri.EscapeDataString("feature/x")}"] = JsonResponse(200, new
        {
            commits = messages.Select(m => new
            {
                commit = new { message = $"{m}\n\nMore details" }
            }).ToArray()
        });

    [Fact]
    public async Task BuildPreview_NoTemplate_EmptyBodyWithCommits()
    {
        StubCommits("Fix login", "Add tests");

        var result = await CreateService().BuildPreviewAsync(
            "acme/repo", "main", "feature/x", "Title", useAI: false, restToken: "ghp_1", copilotToken: null);

        Assert.Equal("", result.Template);
        Assert.Equal(2, result.Commits.Count);
        Assert.Equal("Fix login", result.Commits[0]);
        Assert.Equal("", result.SuggestedBody);
        Assert.Null(result.SummaryError);
    }

    [Fact]
    public async Task BuildPreview_TemplateDecodedAndTicketSubstituted()
    {
        StubTemplate("## 📝 Description\n[LOY-XXX] implement thing");
        StubCommits("Fix");

        var result = await CreateService().BuildPreviewAsync(
            "acme/repo", "main", "feature/LOY-123-fix", "Title", useAI: false, restToken: "ghp_1", copilotToken: null);

        Assert.StartsWith("## 📝 Description", result.Template);
        Assert.Contains("[LOY-123]", result.SuggestedBody);
        Assert.DoesNotContain("[LOY-XXX]", result.SuggestedBody);
    }

    [Fact]
    public async Task BuildPreview_UseAiWithCopilotToken_GeneratesSummary()
    {
        StubTemplate("## 📝 Description\nWhat change does this PR introduce?");
        StubCommits("Fix login");
        _ai.Reply = "This PR fixes the login flow.";

        var result = await CreateService().BuildPreviewAsync(
            "acme/repo", "main", "feature/x", "Title", useAI: true, restToken: "ghp_1", copilotToken: "gho_1");

        Assert.Equal("This PR fixes the login flow.", result.Summary);
        Assert.Null(result.SummaryError);
        Assert.Contains("This PR fixes the login flow.", result.SuggestedBody);
        Assert.Equal("copilot", _ai.LastRequest!.Provider);
        Assert.Equal("gpt-4o", _ai.LastRequest.Model);
        Assert.Equal("gho_1", _ai.LastRequest.OAuthToken);
        Assert.Contains("Fix login", _ai.LastRequest.UserPrompt);
    }

    [Fact]
    public async Task BuildPreview_UseAiWithoutToken_SummaryError()
    {
        StubCommits("Fix login");

        var result = await CreateService().BuildPreviewAsync(
            "acme/repo", "main", "feature/x", "Title", useAI: true, restToken: "ghp_1", copilotToken: null);

        Assert.Equal("", result.Summary);
        Assert.Contains("No OAuth token", result.SummaryError);
        Assert.Null(_ai.LastRequest);
    }

    [Fact]
    public async Task BuildPreview_UseAiEmptySummary_Error()
    {
        StubCommits("Fix login");
        _ai.Reply = "";

        var result = await CreateService().BuildPreviewAsync(
            "acme/repo", "main", "feature/x", "Title", useAI: true, restToken: "ghp_1", copilotToken: "gho_1");

        Assert.Equal("", result.Summary);
        Assert.Contains("empty response", result.SummaryError);
    }

    [Fact]
    public async Task BuildPreview_NoRestToken_NoCommitsNoTemplate()
    {
        var result = await CreateService().BuildPreviewAsync(
            "acme/repo", "main", "feature/x", "Title", useAI: false, restToken: null, copilotToken: null);

        Assert.Empty(result.Commits);
        Assert.Equal("", result.Template);
        Assert.Empty(_github.Responses);
    }

    [Fact]
    public async Task FetchFileContent_DecodesBase64()
    {
        StubTemplate("# Hello");
        var content = await CreateService().FetchFileContent("acme/repo", ".github/PULL_REQUEST_TEMPLATE.md", "ghp_1");
        Assert.Equal("# Hello", content);
    }

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public Dictionary<string, GitHubResponse> Responses { get; } = new();

        public Task<GitHubResponse> GetAsync(string path, string? token = null, CancellationToken ct = default)
            => Task.FromResult(Responses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));

        public Task<GitHubResponse> PostAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));

        public Task<GitHubResponse> PutAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));

        public Task<GitHubResponse> GraphQlAsync(string query, string? token, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));
    }

    private sealed class FakeAiClient : IAiProviderClient
    {
        public string? Reply { get; set; }
        public AiRequest? LastRequest { get; private set; }

        public Task<string?> CompleteAsync(AiRequest request)
        {
            LastRequest = request;
            return Task.FromResult(Reply);
        }
    }
}
