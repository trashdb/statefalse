using System.Text.Json;
using Statefalse.Domain.Contracts;
using Statefalse.Application;
using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// GitHub API proxy endpoints: user branches, create PR (with DB sync),
/// PR preview and natural-language interpret.
/// </summary>
public class GitHubApiService
{
    private readonly IGitHubUserRepository _users;
    private readonly IPullRequestEventRepository _prs;
    private readonly IUnitOfWork _uow;
    private readonly IGitHubClient _github;
    private readonly IGitHubTokenResolver _tokens;
    private readonly PrPreviewService _preview;
    private readonly QueryInterpretationService _interpreter;
    private readonly ILogger<GitHubApiService> _logger;
    private readonly ISignalRNotifier _notifier;

    public GitHubApiService(
        IGitHubUserRepository users,
        IPullRequestEventRepository prs,
        IUnitOfWork uow,
        IGitHubClient github,
        IGitHubTokenResolver tokens,
        PrPreviewService preview,
        QueryInterpretationService interpreter,
        ILogger<GitHubApiService> logger,
        ISignalRNotifier notifier)
    {
        _users = users;
        _prs = prs;
        _uow = uow;
        _github = github;
        _tokens = tokens;
        _preview = preview;
        _interpreter = interpreter;
        _logger = logger;
        _notifier = notifier;
    }

    public async Task<ApiResult> GetMyBranchesAsync(long gitHubId, string repo)
    {
        var user = await _tokens.GetUserAsync(gitHubId);
        var token = _tokens.ResolveForUser(user);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No token" });

        var listResp = await _github.GetAsync($"/repos/{repo}/branches?per_page=100", token);
        if (listResp.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, new { error = "GitHub API unreachable" });
        if (listResp.StatusCode is < 200 or >= 300 || listResp.Body is not { } doc)
            return ApiResult.FromGitHubStatus(listResp.StatusCode, new { error = "GitHub API error" });

        var branches = doc.EnumerateArray();
        var username = user?.GitHubUsername ?? "";

        var myBranches = new List<object>();
        var semaphore = new SemaphoreSlim(10);

        await Parallel.ForEachAsync(branches, async (branch, ct) =>
        {
            var branchName = branch.GetProperty("name").GetString() ?? "";

            if (branchName.StartsWith("dependabot/"))
                return;

            await semaphore.WaitAsync(ct);
            try
            {
                var detailResp = await _github.GetAsync($"/repos/{repo}/branches/{branchName}", token, ct);
                if (detailResp.StatusCode is < 200 or >= 300 || detailResp.Body is not { } detailDoc)
                    return;

                var authorLogin = detailDoc
                    .GetProperty("commit")
                    .GetProperty("author")
                    .GetProperty("login")
                    .GetString();

                if (string.Equals(authorLogin, username, StringComparison.OrdinalIgnoreCase))
                {
                    lock (myBranches)
                    {
                        myBranches.Add(new BranchDto(branchName));
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
        });

        return ApiResult.Ok(myBranches);
    }

    public async Task<ApiResult> CreatePrAsync(long gitHubId, string repo, string head, string baseBranch, string title, string? body, string? subscribers)
    {
        var user = await _tokens.GetUserAsync(gitHubId);
        var token = _tokens.ResolveForUser(user);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No token" });

        _logger.LogInformation("CreatePr started: repo={Repo} head={Head} baseBranch={Base} gitHubId={Id}",
            repo, head, baseBranch, gitHubId);

        var payload = new
        {
            title,
            head,
            @base = baseBranch,
            body = body ?? ""
        };

        var resp = await _github.PostAsync($"/repos/{repo}/pulls", token, payload);
        _logger.LogInformation("GitHub API responded to CreatePr: status={Status}", resp.StatusCode);

        if (resp.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, new { error = "GitHub API unreachable" });

        if (resp.StatusCode is < 200 or >= 300 || resp.Body is not { } doc)
        {
            var msg = resp.Body is { } b && b.TryGetProperty("message", out var m) ? m.GetString() : "Unknown error";

            var detail = msg;
            if (resp.Body is { } b2 && b2.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array)
            {
                foreach (var e in errors.EnumerateArray())
                {
                    if (e.TryGetProperty("message", out var em))
                    {
                        detail = em.GetString() ?? detail;
                        break;
                    }
                }
            }

            detail = TruncateLogValue(detail ?? "Unknown error");
            _logger.LogWarning("CreatePr failed for repo={Repo} head={Head}: status={Status} detail={Detail}",
                repo, head, resp.StatusCode, detail);

            if (resp.StatusCode == StatusCodes.Status422UnprocessableEntity &&
                detail?.Contains("already exists", StringComparison.OrdinalIgnoreCase) == true)
            {
                var existingResp = await _github.GetAsync($"/repos/{repo}/pulls?state=open&per_page=100", token);
                if (existingResp.StatusCode is >= 200 and < 300 && existingResp.Body is { } existingDoc
                    && existingDoc.ValueKind == JsonValueKind.Array)
                {
                    foreach (var pr in existingDoc.EnumerateArray())
                    {
                        if (pr.TryGetProperty("head", out var h) &&
                            h.TryGetProperty("ref", out var r) &&
                            r.GetString() == head)
                        {
                            var existingUrl = pr.GetProperty("html_url").GetString() ?? "";
                            var existingNumber = pr.GetProperty("number").GetInt64();
                            return ApiResult.Ok(new { prNumber = existingNumber, url = existingUrl, existing = true });
                        }
                    }
                }
            }

            return ApiResult.FromGitHubStatus(resp.StatusCode, new { error = detail });
        }

        var prUrl = doc.GetProperty("html_url").GetString() ?? "";
        var prNumber = doc.GetProperty("number").GetInt64();
        var prTitle = doc.TryGetProperty("title", out var t) ? t.GetString() ?? title : title;

        _logger.LogInformation("CreatePr succeeded: repo={Repo} pr={PrNumber}", repo, prNumber);

        // Sync to DB so the PR appears immediately in the active PRs list
        try
        {
            var existing = await _prs.FindByRepoAndPrNumberAsync(repo, prNumber);
            if (existing == null)
            {
                // Resolve subscriber usernames to GitHubIds
                long[] subscriberIds = [];
                if (!string.IsNullOrWhiteSpace(subscribers))
                {
                    var usernames = subscribers.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    var resolved = await _users.FindGitHubIdsByUsernamesAsync(usernames);
                    if (resolved.Count > 0)
                        subscriberIds = resolved.ToArray();
                }

                var ev = new PullRequestEvent
                {
                    PrNumber = prNumber,
                    Title = prTitle,
                    AuthorLogin = user?.GitHubUsername ?? "",
                    AuthorGitHubId = user?.GitHubId ?? gitHubId,
                    RepoFullName = repo,
                    HeadBranch = head,
                    BaseBranch = baseBranch,
                    PrUrl = prUrl,
                    Status = "open",
                    Draft = false,
                    OccurredAt = DateTime.UtcNow,
                    SubscriberIds = IdListSerializer.Serialize(subscriberIds)
                };
                await _prs.AddAsync(ev);
                await _uow.SaveChangesAsync();
                _logger.LogInformation("CreatePr: inserted PullRequestEvent for pr={PrNumber} subscribers={Subscribers}", prNumber, subscriberIds.Length);
            }
            await _notifier.NotifyPullRequestsUpdatedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "CreatePr: failed to sync PR event to DB");
        }

        return ApiResult.Ok(new { prNumber, url = prUrl });
    }

    private static string TruncateLogValue(string value)
    {
        const int maxLength = 256;
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "…";
    }

    public async Task<ApiResult> PrPreviewAsync(long gitHubId, string repo, string head, string baseBranch, string title, bool useAI)
    {
        var user = await _tokens.GetUserAsync(gitHubId);
        var token = _tokens.ResolveForUser(user);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No token" });

        var preview = await _preview.BuildPreviewAsync(repo, baseBranch, head, title, useAI, token, _tokens.ResolveOAuthForUser(user));

        return ApiResult.Ok(new PrPreviewDto(
            preview.Template,
            preview.Commits,
            preview.Summary,
            preview.SuggestedBody,
            preview.SummaryError));
    }

    public async Task<ApiResult> InterpretAsync(InterpretRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
            return ApiResult.BadRequest(new { error = "Query is required" });

        var user = await _tokens.GetUserAsync(request.GitHubId);
        var oauthToken = _tokens.ResolveOAuthForUser(user);
        if (string.IsNullOrEmpty(oauthToken))
            return ApiResult.BadRequest(new { error = "GitHub OAuth is required to use Copilot." });

        var result = await _interpreter.InterpretAsync(request, oauthToken);
        return ApiResult.Ok(result);
    }
}
