using System.Text.Json;
using Statefalse.Domain.Contracts;
using Statefalse.Application;
using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// Workflow run queries + rerun/sync/target operations.
/// </summary>
public class WorkflowService
{
    private readonly IPullRequestEventRepository _prs;
    private readonly IWorkflowRunRepository _runs;
    private readonly IUnitOfWork _uow;
    private readonly IGitHubClient _github;
    private readonly IGitHubTokenResolver _tokens;
    private readonly ISignalRNotifier _notifier;
    private readonly INotificationRepository _notifications;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(
        IPullRequestEventRepository prs,
        IWorkflowRunRepository runs,
        IUnitOfWork uow,
        IGitHubClient github,
        IGitHubTokenResolver tokens,
        ISignalRNotifier notifier,
        INotificationRepository notifications,
        ILogger<WorkflowService> logger)
    {
        _prs = prs;
        _runs = runs;
        _uow = uow;
        _github = github;
        _tokens = tokens;
        _notifier = notifier;
        _notifications = notifications;
        _logger = logger;
    }

    public async Task<ApiResult> GetRunsAsync(long gitHubId, int limit)
    {
        // Get PRs user is subscribed to (including authored)
        var subscribedPrs = await _prs.GetSubscribedToByUserAsync(gitHubId);
        var subscribedRepoBranches = subscribedPrs
            .Select(p => (p.RepoFullName, p.HeadBranch))
            .Where(p => !string.IsNullOrEmpty(p.HeadBranch))
            .ToList();

        var myRuns = await _runs.GetForUserAsync(gitHubId, limit);

        var targetRuns = await _runs.GetTargetRunsAsync(gitHubId, limit * 2);

        var filteredTargetRuns = targetRuns
            .Where(w => IdListSerializer.Deserialize(w.TargetGitHubIds).Contains(gitHubId))
            .Take(limit)
            .ToList();

        // Get runs from PRs user is subscribed to (but not already in myRuns/targetRuns)
        var subscribedRuns = new List<WorkflowRun>();
        if (subscribedRepoBranches.Count > 0)
        {
            var repoList = subscribedRepoBranches.Select(rb => rb.RepoFullName).Distinct().ToList();
            // EF Core can't translate .Any() over a local tuple list; translate the
            // repo filter, then match head branches in memory.
            var candidates = await _runs.GetCandidatesAsync(gitHubId, repoList, limit * 10);
            var branchKeySet = subscribedRepoBranches
                .Where(rb => !string.IsNullOrEmpty(rb.HeadBranch))
                .Select(rb => (rb.RepoFullName, rb.HeadBranch))
                .ToHashSet();
            subscribedRuns = candidates
                .Where(w => w.HeadBranch != null && branchKeySet.Contains((w.Repo, w.HeadBranch)))
                .Take(limit)
                .ToList();
        }

        var allRuns = myRuns.Concat(filteredTargetRuns).Concat(subscribedRuns)
            .DistinctBy(w => w.Id)
            .OrderByDescending(w => w.Id)
            .Take(limit)
            .ToList();

        // Look up PRs matching each run's repo+branch
        var branchKeys = allRuns
            .Where(r => r.HeadBranch != null)
            .Select(r => new { r.Repo, r.HeadBranch })
            .Distinct()
            .ToList();
        var prs = new List<(string repo, string branch, long prNumber, string title)>();
        if (branchKeys.Count != 0)
        {
            var repoList = branchKeys.Select(b => b.Repo).ToList();
            var branchList = branchKeys.Select(b => b.HeadBranch!).ToList();
            var prEvents = await _prs.GetOpenForReposAndBranchesAsync(repoList, branchList);
            prs = prEvents
                .Where(e => e.HeadBranch != null)
                .Select(e => (e.RepoFullName, e.HeadBranch!, e.PrNumber, e.Title ?? ""))
                .ToList();
        }

        return ApiResult.Ok(allRuns.Select(w => new WorkflowRunDto(
            w.Id,
            w.RunId,
            w.WorkflowName,
            w.Repo,
            w.Actor,
            w.HeadBranch,
            w.Trigger,
            w.Status,
            w.HtmlUrl,
            w.StartedAt,
            IdListSerializer.Deserialize(w.TargetGitHubIds),
            w.HeadBranch != null
                ? (int?)prs.FirstOrDefault(p => p.repo == w.Repo && p.branch == w.HeadBranch).prNumber
                : null,
            w.HeadBranch != null
                ? prs.FirstOrDefault(p => p.repo == w.Repo && p.branch == w.HeadBranch).title
                : null)));
    }

    public async Task<ApiResult> SetTargetAsync(int id, long gitHubId, SetTargetRequest request)
    {
        var run = await _runs.FindByIdAsync(id);

        if (run == null)
            return ApiResult.NotFound("Workflow run not found.");

        // Multi-tenant rule: only users that can see the run (own it, are targeted
        // by it, or are subscribed to a matching PR) may change its targets.
        var canManage = run.GitHubId == gitHubId
            || IdListSerializer.Deserialize(run.TargetGitHubIds).Contains(gitHubId)
            || await _prs.AnyOpenForRepoAndBranchByUserAsync(run.Repo, run.HeadBranch, gitHubId);
        if (!canManage)
            return ApiResult.Forbid("You don't have access to this workflow run.");

        run.TargetGitHubIds = IdListSerializer.Serialize(request.TargetGitHubIds ?? []);
        await _uow.SaveChangesAsync();

        return ApiResult.Ok(new { runId = run.RunId, targetGitHubIds = IdListSerializer.Deserialize(run.TargetGitHubIds) });
    }

    public async Task<ApiResult> RerunAsync(long runId, long gitHubId)
    {
        var run = await _runs.FindLatestByRunIdAsync(runId);
        if (run == null)
            return ApiResult.NotFound("Workflow run not found.");

        var token = await _tokens.ResolveAsync(gitHubId);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized("No access token available.");

        var response = await _github.PostAsync($"/repos/{run.Repo}/actions/runs/{run.RunId}/rerun", token, new { });
        if (response.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, "GitHub API unreachable");
        if (response.StatusCode is < 200 or >= 300)
        {
            var detail = SafeGitHubError(response.Body);
            _logger.LogWarning("GitHub rerun failed: status={Status} detail={Detail}", response.StatusCode, detail);
            return ApiResult.FromGitHubStatus(response.StatusCode, new { error = detail });
        }

        // Create an in_progress record immediately so syncFromApi picks it up
        var newRun = new WorkflowRun
        {
            RunId = run.RunId,
            GitHubId = gitHubId,
            WorkflowName = run.WorkflowName,
            Repo = run.Repo,
            Actor = run.Actor,
            HeadBranch = run.HeadBranch,
            Trigger = "workflow_dispatch",
            HtmlUrl = run.HtmlUrl,
            Status = "in_progress",
            StartedAt = DateTime.UtcNow
        };
        await _runs.AddAsync(newRun);
        await _uow.SaveChangesAsync();

        await _notifier.NotifyUserAsync(gitHubId, "WorkflowRunStarted", new
        {
            id = newRun.Id,
            runId = newRun.RunId,
            workflowName = newRun.WorkflowName,
            repo = newRun.Repo,
            branch = newRun.HeadBranch,
            trigger = newRun.Trigger,
            actor = newRun.Actor,
            htmlUrl = newRun.HtmlUrl
        });

        await _notifier.NotifyPullRequestsUpdatedAsync();

        return ApiResult.Ok(new { rerun = true });
    }

    private static string SafeGitHubError(JsonElement? body)
    {
        if (body is { } document && document.ValueKind == JsonValueKind.Object
            && document.TryGetProperty("message", out var message)
            && message.ValueKind == JsonValueKind.String)
        {
            var value = message.GetString()?.ReplaceLineEndings(" ").Trim() ?? "Rerun failed";
            return value.Length <= 256 ? value : value[..256] + "…";
        }

        return "Rerun failed";
    }

    public async Task<ApiResult> SyncActiveAsync(long gitHubId)
    {
        var token = await _tokens.ResolveAsync(gitHubId);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized("No access token available.");

        // Scope to the caller's PRs (author or subscriber) — never query the
        // whole DB across all tenants.
        var repos = await _prs.GetSubscribedReposAsync(gitHubId);

        if (repos.Count == 0)
            return ApiResult.Ok(new { synced = 0, repos = 0, message = "No active PRs found." });

        var newCount = 0;
        var reconciledCount = 0;
        foreach (var repo in repos)
        {
            var response = await _github.GetAsync($"/repos/{repo}/actions/runs?status=in_progress&per_page=10", token);
            if (response.StatusCode >= 200 && response.StatusCode < 300 && response.Body is { } doc)
            {
                foreach (var run in doc.GetProperty("workflow_runs").EnumerateArray())
                {
                    var runId = run.GetProperty("id").GetInt64();
                    var name = run.TryGetProperty("name", out var wn) ? wn.GetString() : "Workflow";
                    var isIgnored = IgnoredWorkflows.IsIgnored(name);

                    var exists = await _runs.AnyInProgressByRunIdAsync(runId);
                    if (exists) continue;

                    var actor = run.TryGetProperty("actor", out var act)
                        ? act.GetProperty("login").GetString() ?? "unknown"
                        : "unknown";
                    var branch = run.TryGetProperty("head_branch", out var hb) ? hb.GetString() : null;
                    var htmlUrl = run.TryGetProperty("html_url", out var hu) ? hu.GetString() : null;
                    var startedAt = run.TryGetProperty("run_started_at", out var rsa)
                        ? rsa.GetDateTime()
                        : DateTime.UtcNow;
                    var trigger = run.TryGetProperty("event", out var ev) ? ev.GetString() : null;

                    await _runs.AddAsync(new WorkflowRun
                    {
                        RunId = runId,
                        GitHubId = gitHubId,
                        WorkflowName = name,
                        Repo = repo,
                        Actor = actor,
                        HeadBranch = branch,
                        Trigger = trigger,
                        HtmlUrl = htmlUrl,
                        Status = "in_progress",
                        StartedAt = startedAt,
                        IsIgnored = isIgnored
                    });
                    newCount++;
                }
            }

            // A missed workflow_run.completed webhook otherwise leaves the local
            // row in_progress forever because GitHub no longer returns it from
            // the active-runs query above.
            var completedResponse = await _github.GetAsync($"/repos/{repo}/actions/runs?status=completed&per_page=20", token);
            if (completedResponse.StatusCode is < 200 or >= 300 || completedResponse.Body is not { } completedDoc)
                continue;

            foreach (var run in completedDoc.GetProperty("workflow_runs").EnumerateArray())
            {
                var runId = run.GetProperty("id").GetInt64();
                var dbRun = await _runs.FindLatestInProgressByRunIdAsync(runId);
                if (dbRun == null) continue;

                var conclusion = run.TryGetProperty("conclusion", out var conclusionElement)
                    ? conclusionElement.GetString()
                    : null;
                var dbStatus = WorkflowConclusionMapper.ToDbStatus(conclusion);
                if (dbStatus == null) continue;

                var name = run.TryGetProperty("name", out var workflowNameElement)
                    ? workflowNameElement.GetString() ?? dbRun.WorkflowName ?? "Workflow"
                    : dbRun.WorkflowName ?? "Workflow";
                var actor = run.TryGetProperty("actor", out var actorElement)
                    && actorElement.TryGetProperty("login", out var loginElement)
                    ? loginElement.GetString() ?? dbRun.Actor
                    : dbRun.Actor;
                var htmlUrl = run.TryGetProperty("html_url", out var urlElement)
                    ? urlElement.GetString() ?? dbRun.HtmlUrl
                    : dbRun.HtmlUrl;

                dbRun.Status = dbStatus;
                dbRun.IsIgnored = IgnoredWorkflows.IsIgnored(name);
                dbRun.WorkflowName ??= name;
                dbRun.Actor = actor;
                dbRun.HtmlUrl ??= htmlUrl;

                await _uow.SaveChangesAsync();

                if (!dbRun.IsIgnored)
                {
                    if (dbStatus == "failure")
                    {
                        await _notifications.AddAsync(new Notification
                        {
                            RecipientGitHubId = gitHubId,
                            Kind = "workflow_failed",
                            Title = "Workflow Failed",
                            Body = $"{name} failed for {actor} in {repo}",
                            Repo = repo,
                            PrUrl = htmlUrl,
                            CreatedAt = DateTime.UtcNow
                        });
                        await _uow.SaveChangesAsync();
                    }

                    await _notifier.NotifyUserAsync(gitHubId, "WorkflowRunCompleted", new WorkflowRunCompletedPayload(
                        RunId: runId,
                        Succeeded: dbStatus == "success",
                        Conclusion: conclusion,
                        WorkflowName: name,
                        Repo: repo,
                        Actor: actor,
                        HtmlUrl: htmlUrl,
                        Trigger: dbRun.Trigger));
                }

                reconciledCount++;
            }
        }

        if (newCount > 0)
            await _uow.SaveChangesAsync();

        return ApiResult.Ok(new
        {
            synced = newCount + reconciledCount,
            discovered = newCount,
            reconciled = reconciledCount,
            repos = repos.Count
        });
    }
}
