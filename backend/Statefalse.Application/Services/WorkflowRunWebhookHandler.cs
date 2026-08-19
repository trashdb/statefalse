using System.Text.Json;
using Statefalse.Domain.Contracts;
using Statefalse.Application;
using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// workflow_run webhook handler: tracks in_progress/completed runs, supersedes
/// stale siblings, persists punishment events for failures and fans out SignalR.
/// </summary>
public class WorkflowRunWebhookHandler : IWebhookHandler
{
    private readonly IWorkflowRunRepository _runs;
    private readonly IPunishmentEventRepository _punishments;
    private readonly IUnitOfWork _uow;
    private readonly IGitHubTokenResolver _tokens;
    private readonly ISignalRNotifier _notifier;
    private readonly INotificationRepository _notifications;
    private readonly ILogger<WorkflowRunWebhookHandler> _logger;

    public WorkflowRunWebhookHandler(
        IWorkflowRunRepository runs,
        IPunishmentEventRepository punishments,
        IUnitOfWork uow,
        IGitHubTokenResolver tokens,
        ISignalRNotifier notifier,
        INotificationRepository notifications,
        ILogger<WorkflowRunWebhookHandler> logger)
    {
        _runs = runs;
        _punishments = punishments;
        _uow = uow;
        _tokens = tokens;
        _notifier = notifier;
        _notifications = notifications;
        _logger = logger;
    }

    public string EventType => "workflow_run";

    public async Task<ApiResult> HandleAsync(JsonElement payload)
    {
        var action = payload.GetProperty("action").GetString();
        var repo = WebhookPayload.TryGetRepo(payload);
        var name = WebhookPayload.TryGetWorkflowName(payload);

        if (action is "in_progress" or "requested") return await HandleInProgress(payload);
        if (action == "completed") return await HandleCompleted(payload);

        WebhookLog.Log("workflow_run", action, repo, name, "ignored", $"Unsupported action '{action}'");
        return ApiResult.Ok($"Ignored: workflow_run action '{action}'.");
    }

    private async Task<ApiResult> HandleInProgress(JsonElement payload)
    {
        var run = payload.GetProperty("workflow_run");
        var culprit = ResolveCulprit(payload);
        if (culprit == null)
        {
            WebhookLog.Log("workflow_run", "in_progress", WebhookPayload.TryGetRepo(payload), WebhookPayload.TryGetWorkflowName(payload), "ignored", "Could not resolve actor");
            return ApiResult.Ok("Could not resolve actor.");
        }

        var repo = WebhookPayload.GetRepoOrUnknown(payload);
        var p = ParseRun(run);
        var name = p.Name ?? "Workflow";
        var isIgnored = IgnoredWorkflows.IsIgnored(p.Name);
        var branch = p.Branch;
        var headSha = p.HeadSha;
        var url = p.Url;
        var runId = p.RunId;
        var startedAt = p.StartedAt;
        var trigger = p.Trigger;

        var existingInProgress = await _runs.FindInProgressByRunIdAsync(runId);
        if (existingInProgress != null)
        {
            // Already tracking this run — likely a duplicate webhook event
            await _uow.SaveChangesAsync();
            return ApiResult.Ok(new { runId });
        }

        var gitHubId = culprit.Id ?? (await _tokens.FindByLoginAsync(culprit.Login))?.GitHubId;
        var newRun = new WorkflowRun
        {
            RunId = runId,
            GitHubId = gitHubId ?? 0,
            WorkflowName = name,
            Repo = repo,
            Actor = culprit.Login,
            HeadBranch = branch,
            HeadSha = headSha,
            Trigger = trigger,
            HtmlUrl = url,
            Status = "in_progress",
            StartedAt = startedAt,
            IsIgnored = isIgnored
        };
        await _runs.AddAsync(newRun);
        await _uow.SaveChangesAsync();

        // Mark previous in_progress runs for same repo+workflow+branch as superseded
        // (GitHub does not send completed webhooks for superseded runs)
        if (branch != null && name != null)
        {
            var superseded = await _runs.FindSupersededAsync(newRun.Id, repo, name, branch);
            if (superseded.Count > 0)
            {
                foreach (var s in superseded)
                    s.Status = "superseded";
                await _uow.SaveChangesAsync();
                _logger.LogInformation("Superseded {Count} previous run(s) for {Repo} {Name} on {Branch}", superseded.Count, repo, name, branch);
            }
        }

        // Notify via SignalR only for non-ignored workflows
        if (!isIgnored)
        {
            var user = await _tokens.FindConnectedUserAsync(culprit.Login, culprit.Id);
            if (user != null)
            {
                await _notifier.NotifyUserAsync(user.GitHubId, "WorkflowRunStarted", new WorkflowRunStartedPayload(
                    Id: newRun.Id,
                    RunId: runId,
                    WorkflowName: name,
                    Repo: repo,
                    Branch: branch,
                    Trigger: trigger,
                    Actor: culprit.Login,
                    HtmlUrl: url));
                _logger.LogInformation("Running workflow {RunId} notified to {Login}", runId, culprit.Login);
            }
        }

        // Always notify PR update so ciStatus refreshes even for ignored workflows
        await _notifier.NotifyPullRequestsUpdatedAsync();

        var actor = culprit.Login;
        WebhookLog.Log("workflow_run", "in_progress", repo, name, isIgnored ? "ignored" : "processed", $"actor={actor}, runId={runId}");
        return ApiResult.Ok(new { runId });
    }

    private async Task<ApiResult> HandleCompleted(JsonElement payload)
    {
        var workflowRun = payload.GetProperty("workflow_run");
        var conclusion = workflowRun.GetProperty("conclusion").GetString();

        var culprit = ResolveCulprit(payload);
        if (culprit == null)
        {
            _logger.LogWarning("Could not determine culprit for failed workflow run.");
            return ApiResult.Ok("Could not resolve culprit.");
        }

        var repoFullName = WebhookPayload.GetRepoOrUnknown(payload);
        var p = ParseRun(workflowRun);
        var runId = p.RunId;
        var workflowName = p.Name;
        var isIgnored = IgnoredWorkflows.IsIgnored(workflowName);
        var workflowUrl = p.Url;
        var trigger = p.Trigger;

        var dbStatus = WorkflowConclusionMapper.ToDbStatus(conclusion);

        // Update the latest in_progress row for this runId
        var dbRun = await _runs.FindLatestInProgressByRunIdAsync(runId);

        if (dbRun == null && await _runs.FindLatestByRunIdAsync(runId) is { } completedRun
            && completedRun.Status != "in_progress")
        {
            _logger.LogInformation("Ignoring duplicate completion webhook for already terminal run {RunId}", runId);
            return ApiResult.Ok(new { runId, duplicate = true });
        }

        if (dbRun != null)
        {
            if (dbStatus != null)
                dbRun.Status = dbStatus;
            dbRun.IsIgnored = isIgnored;
        }
        else if (dbStatus != null)
        {
            var gitHubId = culprit.Id ?? (await _tokens.FindByLoginAsync(culprit.Login))?.GitHubId;
            await _runs.AddAsync(new WorkflowRun
            {
                RunId = runId,
                GitHubId = gitHubId ?? 0,
                WorkflowName = workflowName,
                Repo = repoFullName,
                Actor = culprit.Login,
                HeadBranch = workflowRun.TryGetProperty("head_branch", out var hb) ? hb.GetString() : null,
                HeadSha = workflowRun.TryGetProperty("head_sha", out var hs) ? hs.GetString() : null,
                Trigger = trigger,
                HtmlUrl = workflowUrl,
                Status = dbStatus,
                StartedAt = DateTime.UtcNow,
                IsIgnored = isIgnored
            });
        }

        await _uow.SaveChangesAsync();

        // Always notify PR update so ciStatus refreshes for ignored workflows too
        await _notifier.NotifyPullRequestsUpdatedAsync();

        // Skip SignalR completion notifications for ignored workflows
        if (isIgnored) return ApiResult.Ok(new { runId });

        // Notify both the culprit and the target user (if set) via SignalR
        async Task NotifyCompleted(long gitHubId, bool succeeded)
        {
            if (!succeeded)
            {
                var workflowTitle = workflowName ?? "Workflow";
                var notification = new Notification
                {
                    RecipientGitHubId = gitHubId,
                    Kind = "workflow_failed",
                    Title = "Workflow Failed",
                    Body = $"{workflowTitle} failed for {culprit.Login} in {repoFullName}",
                    Repo = repoFullName,
                    PrUrl = workflowUrl,
                    CreatedAt = DateTime.UtcNow
                };
                await _notifications.AddAsync(notification);
                await _uow.SaveChangesAsync();
                await _notifier.NotifyUserAsync(gitHubId, "NotificationCreated", new NotificationPayload(
                    Id: notification.Id,
                    Kind: notification.Kind,
                    Title: notification.Title,
                    Body: notification.Body,
                    Repo: notification.Repo,
                    PrNumber: notification.PrNumber,
                    PrUrl: notification.PrUrl,
                    CreatedAt: notification.CreatedAt,
                    IsRead: notification.IsRead));
            }

            await _notifier.NotifyUserAsync(gitHubId, "WorkflowRunCompleted", new WorkflowRunCompletedPayload(
                RunId: runId,
                Succeeded: succeeded,
                Conclusion: conclusion,
                WorkflowName: workflowName,
                Repo: repoFullName,
                Actor: culprit.Login,
                HtmlUrl: workflowUrl,
                Trigger: trigger));
        }

        async Task NotifyCulpritAndTargetsAsync(bool succeeded, GitHubUser? connected)
        {
            if (connected != null)
            {
                await NotifyCompleted(connected.GitHubId, succeeded);
                _logger.LogInformation(succeeded
                    ? "Workflow success notified to {Login}"
                    : "Punishment sent to {Login}", culprit.Login);
            }

            var culpritId = culprit.Id ?? (await _tokens.FindByLoginAsync(culprit.Login))?.GitHubId;
            if (culpritId.HasValue && culpritId.Value != connected?.GitHubId)
            {
                await NotifyCompleted(culpritId.Value, succeeded);
                _logger.LogInformation(succeeded
                    ? "Workflow success persisted for {Login} without active connection"
                    : "Punishment persisted for {Login} without active connection", culprit.Login);
            }

            var targetIds = IdListSerializer.Deserialize(dbRun?.TargetGitHubIds);
            foreach (var tid in targetIds)
            {
                if (tid != connected?.GitHubId)
                {
                    await NotifyCompleted(tid, succeeded);
                    _logger.LogInformation(succeeded
                        ? "Workflow success also notified to target {TargetId}"
                        : "Punishment also notified to target {TargetId}", tid);
                }
            }
        }

        if (conclusion == "success")
        {
            var user = await _tokens.FindConnectedUserAsync(culprit.Login, culprit.Id);
            await NotifyCulpritAndTargetsAsync(true, user);

            WebhookLog.Log("workflow_run", "completed", repoFullName, workflowName, "processed", $"conclusion={conclusion}, notified");
            return ApiResult.Ok(new { runId, conclusion });
        }

        if (WorkflowConclusionMapper.IsNonFailure(conclusion))
        {
            WebhookLog.Log("workflow_run", "completed", repoFullName, workflowName, "processed", $"conclusion={conclusion}, no notification (non-failure)");
            return ApiResult.Ok(new { runId, conclusion });
        }

        // Save punishment event (always for failures)
        var historyEvent = new PunishmentEvent
        {
            RunId = runId, CulpritLogin = culprit.Login, CulpritGitHubId = culprit.Id,
            RepoFullName = repoFullName, WorkflowName = workflowName, WorkflowUrl = workflowUrl,
            OccurredAt = DateTime.UtcNow
        };

        var user2 = await _tokens.FindConnectedUserAsync(culprit.Login, culprit.Id);
        historyEvent.WasNotified = user2 != null;
        await _punishments.AddAsync(historyEvent);
        await _uow.SaveChangesAsync();

        await NotifyCulpritAndTargetsAsync(false, user2);

        WebhookLog.Log("workflow_run", "completed", repoFullName, workflowName, "processed", $"conclusion={conclusion}, failure handled");
        return ApiResult.Ok(new { runId, conclusion });
    }

    private CulpritInfo? ResolveCulprit(JsonElement payload)
    {
        try
        {
            var run = payload.GetProperty("workflow_run");

            if (run.TryGetProperty("pull_requests", out var prs) && prs.GetArrayLength() > 0)
            {
                var pr = prs[0];

                if (pr.TryGetProperty("merged_by", out var mergedBy) && CulpritFromUser(mergedBy) is { } mb)
                    return mb;

                if (pr.TryGetProperty("user", out var prUser) && CulpritFromUser(prUser) is { } pu)
                    return pu;
            }

            if (payload.TryGetProperty("sender", out var sender) && CulpritFromUser(sender) is { } s)
                return s;

            if (run.TryGetProperty("head_commit", out var commit) &&
                commit.ValueKind != JsonValueKind.Null &&
                commit.TryGetProperty("author", out var author))
            {
                var username = author.TryGetProperty("username", out var uname)
                    ? uname.GetString()
                    : author.TryGetProperty("name", out var name) ? name.GetString() : null;

                if (!string.IsNullOrEmpty(username))
                    return new CulpritInfo(username, null);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving culprit from webhook payload.");
        }

        return null;
    }

    /// <summary>Extracts {id, login} from a GitHub user object, or null when missing.</summary>
    private static CulpritInfo? CulpritFromUser(JsonElement user)
    {
        if (user.ValueKind != JsonValueKind.Object) return null;
        var id = user.TryGetProperty("id", out var idProp) ? idProp.GetInt64() : (long?)null;
        var login = user.TryGetProperty("login", out var loginProp) ? loginProp.GetString() : null;
        return string.IsNullOrEmpty(login) ? null : new CulpritInfo(login, id);
    }

    /// <summary>Reads the common workflow_run fields shared by in_progress and completed events.</summary>
    private sealed record RunPayload(
        long RunId, string? Name, string? Branch, string? HeadSha, string? Url,
        string? Trigger, DateTime StartedAt);

    private static RunPayload ParseRun(JsonElement run)
    {
        return new RunPayload(
            RunId: run.GetProperty("id").GetInt64(),
            Name: run.TryGetProperty("name", out var wn) ? wn.GetString() : null,
            Branch: run.TryGetProperty("head_branch", out var hb) ? hb.GetString() : null,
            HeadSha: run.TryGetProperty("head_sha", out var hs) ? hs.GetString() : null,
            Url: run.TryGetProperty("html_url", out var hu) ? hu.GetString() : null,
            Trigger: run.TryGetProperty("event", out var ev) ? ev.GetString() : null,
            StartedAt: run.TryGetProperty("run_started_at", out var rsa) ? rsa.GetDateTime() : DateTime.UtcNow);
    }
}

internal record CulpritInfo(string Login, long? Id);
