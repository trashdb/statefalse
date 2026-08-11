using System.Globalization;
using System.Text.Json;
using Statefalse.Domain.Contracts;
using Statefalse.Application;

namespace Statefalse.Application;

/// <summary>
/// Pull request read paths: active list (with self-healing + ciStatus), detail
/// and commits/files/checks proxies.
/// </summary>
public class PullRequestQueryService
{
    private readonly IPullRequestEventRepository _prs;
    private readonly IWorkflowRunRepository _runs;
    private readonly ICheckSuiteEventRepository _checkSuites;
    private readonly IUnitOfWork _uow;
    private readonly IGitHubClient _github;
    private readonly IGitHubTokenResolver _tokens;
    private readonly PullRequestSyncService _sync;
    private readonly ILogger<PullRequestQueryService> _logger;

    private sealed record PrRow(
        long PrNumber,
        string Title,
        string RepoFullName,
        string? HeadBranch,
        string? BaseBranch,
        string? PrUrl,
        string Status,
        string? Conclusion,
        bool Draft,
        bool ReviewApproved,
        string? LastCommentBy,
        string? LastCommentBody,
        DateTime? LastCommentAt,
        string? LastCommentUrl,
        string? LastReviewFilePath,
        int? LastReviewLine,
        string? SubscriberIds,
        long? AuthorGitHubId);

    private sealed record PullRequestLiveData(
        long PrNumber,
        string Repo,
        bool? Draft,
        string? MergeableState,
        string? HeadSha,
        string? State,
        bool Merged,
        DateTime? MergedAt);

    private sealed record RunInfo(string Repo, string? HeadSha, string? WorkflowName, int Id, string Status);

    private sealed record CheckSuiteInfo(string Repo, string HeadSha, int Id, string Conclusion);

    public PullRequestQueryService(
        IPullRequestEventRepository prs,
        IWorkflowRunRepository runs,
        ICheckSuiteEventRepository checkSuites,
        IUnitOfWork uow,
        IGitHubClient github,
        IGitHubTokenResolver tokens,
        PullRequestSyncService sync,
        ILogger<PullRequestQueryService> logger)
    {
        _prs = prs;
        _runs = runs;
        _checkSuites = checkSuites;
        _uow = uow;
        _github = github;
        _tokens = tokens;
        _sync = sync;
        _logger = logger;
    }

    // ─────────────────────────── Active PR list ───────────────────────────

    public async Task<ApiResult> GetActiveAsync(long gitHubId, int page, int pageSize)
    {
        var user = await _tokens.GetUserAsync(gitHubId);
        var token = _tokens.ResolveForUser(user);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No token" });

        var entities = await _prs.GetActiveForUserAsync(gitHubId, page, pageSize, DateTime.UtcNow.AddHours(-24));
        var prs = entities
            .Select(e => new PrRow(
                e.PrNumber, e.Title, e.RepoFullName, e.HeadBranch, e.BaseBranch, e.PrUrl,
                e.Status, e.Conclusion, e.Draft, e.ReviewApproved, e.LastCommentBy,
                e.LastCommentBody, e.LastCommentAt, e.LastCommentUrl, e.LastReviewFilePath,
                e.LastReviewLine, e.SubscriberIds, e.AuthorGitHubId))
            // Duplicate webhook deliveries can leave multiple rows for one PR; keep the newest.
            .GroupBy(p => (p.RepoFullName, p.PrNumber))
            .Select(g => g.First())
            .ToList();

        // Live PR state (draft, mergeable, headSha, merged_at) fetched from GitHub
        // in parallel — previously N sequential round-trips.
        var liveData = await FetchPullRequestDataAsync(prs, token);

        // Self-heal: correct rows GitHub reports closed/merged and stale merge
        // timestamps so a merged PR never renders as "ready".
        var statusOverrides = await SelfHealPrStatesAsync(prs, liveData);

        // Sync workflow run states from GitHub check-runs for each unique (repo, headSha).
        // Covers webhooks missed while the tunnel was down.
        var shaRepoSet = liveData.Values
            .Where(d => d.HeadSha != null)
            .Select(d => (d.Repo, d.HeadSha!))
            .ToHashSet();
        foreach (var (repo, sha) in shaRepoSet)
        {
            await _sync.SyncCheckRunsForCommit(repo, sha, token);
        }

        // Review approval state from GitHub (webhook may miss approvals when a
        // "commented" review lands after an approval). Fetched in parallel.
        var reviewOverrides = await FetchReviewApprovalsAsync(prs, statusOverrides, token);

        // Re-fetch all workflow runs + check suites after sync, in two batched queries.
        var repos = prs.Select(p => p.RepoFullName).Distinct().ToList();
        var allRuns = await LoadRunsAsync(repos);
        var checkSuites = await LoadCheckSuitesAsync(shaRepoSet);

        var results = new List<PullRequestDto>();
        foreach (var pr in prs)
        {
            var data = liveData.GetValueOrDefault((pr.RepoFullName, pr.PrNumber));
            var effectiveStatus = statusOverrides.GetValueOrDefault(pr.PrNumber, pr.Status);
            var finalReviewApproved = reviewOverrides.GetValueOrDefault(pr.PrNumber, pr.ReviewApproved);

            var prRuns = data?.HeadSha != null
                ? allRuns
                    .Where(r => r.Repo == pr.RepoFullName && r.HeadSha == data.HeadSha)
                    .Select(r => (r.Id, r.WorkflowName, r.Status))
                    .ToList()
                : [];
            var ciStatus = CiStatusCalculator.Calculate(
                data?.HeadSha,
                isOpen: effectiveStatus == "open",
                reviewApproved: finalReviewApproved,
                prRuns);

            var conclusion = ResolveConclusion(pr.Conclusion, data?.HeadSha, pr.RepoFullName, allRuns, checkSuites);
            var subscriberIds = IdListSerializer.Deserialize(pr.SubscriberIds);

            results.Add(new PullRequestDto(
                pr.PrNumber,
                pr.Title,
                pr.RepoFullName,
                pr.HeadBranch,
                pr.BaseBranch,
                pr.PrUrl,
                effectiveStatus,
                conclusion,
                pr.Draft,
                data?.MergeableState,
                ciStatus,
                finalReviewApproved,
                pr.LastCommentBy,
                pr.LastCommentBody,
                pr.LastCommentAt,
                pr.LastCommentUrl,
                pr.LastReviewFilePath,
                pr.LastReviewLine,
                subscriberIds.Contains(gitHubId),
                subscriberIds,
                pr.AuthorGitHubId));
        }

        return ApiResult.Ok(results);
    }

    // ─────────────────────────── PR detail ───────────────────────────

    public async Task<ApiResult> GetDetailAsync(long prNumber, string repo, long gitHubId)
    {
        var token = await _tokens.ResolveAsync(gitHubId);

        var prEvent = await _prs.FindLatestAsync(prNumber, repo);

        int? behindBy = null, aheadBy = null;
        string? mergeableState = null;

        try
        {
            var response = await _github.GetAsync($"/repos/{repo}/pulls/{prNumber}", token);
            if (response.StatusCode is >= 200 and < 300 && response.Body is { } body)
            {
                if (body.TryGetProperty("mergeable_state", out var ms))
                    mergeableState = ms.GetString();

                var headSha = body.GetProperty("head").GetProperty("sha").GetString();
                var baseRef = body.GetProperty("base").GetProperty("ref").GetString();

                if (headSha != null && baseRef != null)
                {
                    var compareResp = await _github.GetAsync($"/repos/{repo}/compare/{baseRef}...{headSha}", token);
                    if (compareResp.StatusCode is >= 200 and < 300 && compareResp.Body is { } compareData)
                    {
                        if (compareData.TryGetProperty("behind_by", out var bb)) behindBy = bb.GetInt32();
                        if (compareData.TryGetProperty("ahead_by", out var ab)) aheadBy = ab.GetInt32();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetDetail failed for PR {PrNumber} in {Repo}", prNumber, repo);
        }

        return ApiResult.Ok(new PullRequestDetailDto(
            prNumber,
            repo,
            mergeableState,
            behindBy,
            aheadBy,
            prEvent?.Title,
            prEvent?.HeadBranch,
            prEvent?.BaseBranch,
            prEvent?.Status,
            prEvent?.Draft ?? false,
            prEvent?.LastCommentBy,
            prEvent?.LastCommentBody,
            prEvent?.LastCommentAt,
            prEvent?.LastCommentUrl,
            prEvent?.LastReviewFilePath,
            prEvent?.LastReviewLine));
    }

    // ─────────────────────────── Commits / Files / Checks ───────────────────────────

    public async Task<ApiResult> GetCommitsAsync(long prNumber, string repo, long gitHubId)
    {
        var token = await _tokens.ResolveAsync(gitHubId);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No access token" });

        var resp = await _github.GetAsync($"/repos/{repo}/pulls/{prNumber}/commits?per_page=30", token);
        if (resp.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, new { error = "GitHub API unreachable" });
        if (resp.StatusCode is < 200 or >= 300 || resp.Body is not { } body)
            return ApiResult.FromGitHubStatus(resp.StatusCode, new { error = "Failed to fetch commits", detail = resp.Body });

        var commits = body.EnumerateArray().Select(c => new CommitDto(
            c.GetProperty("sha").GetString(),
            c.GetProperty("commit").GetProperty("message").GetString(),
            c.GetProperty("commit").GetProperty("author").GetProperty("name").GetString(),
            c.TryGetProperty("author", out var a) && a.ValueKind == JsonValueKind.Object
                ? (a.TryGetProperty("login", out var l) ? l.GetString() : null) : null,
            c.GetProperty("commit").GetProperty("author").GetProperty("date").GetString(),
            c.TryGetProperty("html_url", out var hu) ? hu.GetString() : null)).ToList();

        return ApiResult.Ok(commits);
    }

    public async Task<ApiResult> GetFilesAsync(long prNumber, string repo, long gitHubId)
    {
        var token = await _tokens.ResolveAsync(gitHubId);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No access token" });

        var resp = await _github.GetAsync($"/repos/{repo}/pulls/{prNumber}/files?per_page=30", token);
        if (resp.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, new { error = "GitHub API unreachable" });
        if (resp.StatusCode is < 200 or >= 300 || resp.Body is not { } body)
            return ApiResult.FromGitHubStatus(resp.StatusCode, new { error = "Failed to fetch files", detail = resp.Body });

        var files = body.EnumerateArray().Select(f => new PrFileDto(
            f.GetProperty("filename").GetString(),
            f.GetProperty("status").GetString(),
            f.GetProperty("additions").GetInt32(),
            f.GetProperty("deletions").GetInt32())).ToList();

        return ApiResult.Ok(files);
    }

    public async Task<ApiResult> GetChecksAsync(long prNumber, string repo, long gitHubId)
    {
        var token = await _tokens.ResolveAsync(gitHubId);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No access token" });

        // First get PR to get head SHA
        var prResp = await _github.GetAsync($"/repos/{repo}/pulls/{prNumber}", token);
        if (prResp.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, new { error = "GitHub API unreachable" });
        if (prResp.StatusCode is < 200 or >= 300 || prResp.Body is not { } prDoc)
            return ApiResult.FromGitHubStatus(prResp.StatusCode, new { error = "Failed to fetch PR", detail = prResp.Body });

        var headSha = prDoc.GetProperty("head").GetProperty("sha").GetString();
        if (string.IsNullOrEmpty(headSha))
            return ApiResult.Ok(Array.Empty<CheckRunDto>());

        // Now fetch check runs for that SHA
        var crResp = await _github.GetAsync($"/repos/{repo}/commits/{headSha}/check-runs?per_page=100", token);
        if (crResp.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, new { error = "GitHub API unreachable" });
        if (crResp.StatusCode is < 200 or >= 300 || crResp.Body is not { } crDoc)
            return ApiResult.FromGitHubStatus(crResp.StatusCode, new { error = "Failed to fetch check runs", detail = crResp.Body });

        if (!crDoc.TryGetProperty("check_runs", out var checkRunsProp))
            return ApiResult.Ok(Array.Empty<CheckRunDto>());

        var checks = checkRunsProp.EnumerateArray().Select(cr => new CheckRunDto(
            cr.GetProperty("name").GetString(),
            cr.GetProperty("status").GetString(),
            cr.TryGetProperty("conclusion", out var conc) ? conc.GetString() : null,
            cr.TryGetProperty("started_at", out var sa) ? sa.GetString() : null,
            cr.TryGetProperty("completed_at", out var ca) ? ca.GetString() : null,
            cr.TryGetProperty("html_url", out var hu) ? hu.GetString() : null)).ToList();

        return ApiResult.Ok(checks);
    }

    // ─────────────────────────── Live state fetch (parallel) ───────────────────────────

    /// <summary>
    /// Fetches draft/mergeable/headSha/state/mergedAt for every PR in parallel.
    /// </summary>
    private async Task<Dictionary<(string Repo, long PrNumber), PullRequestLiveData>> FetchPullRequestDataAsync(List<PrRow> prs, string? token)
    {
        var tasks = prs.Select(pr => FetchPullRequestDataAsync(pr, token)).ToList();
        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => (r.Repo, r.PrNumber));
    }

    private async Task<PullRequestLiveData> FetchPullRequestDataAsync(PrRow pr, string? token)
    {
        try
        {
            var response = await _github.GetAsync($"/repos/{pr.RepoFullName}/pulls/{pr.PrNumber}", token);
            if (response.StatusCode is < 200 or >= 300 || response.Body is not { } data)
                return new PullRequestLiveData(pr.PrNumber, pr.RepoFullName, null, null, null, null, false, null);

            bool? draft = data.TryGetProperty("draft", out var draftProp) ? draftProp.GetBoolean() : null;

            string? mergeableState = data.TryGetProperty("mergeable_state", out var ms) ? ms.GetString() : null;

            string? headSha = null;
            if (data.TryGetProperty("head", out var head) && head.TryGetProperty("sha", out var sha))
                headSha = sha.GetString();

            // Real open/closed state + whether it was merged — used to self-heal
            // the DB when a close/merge webhook was missed (e.g. tunnel was down).
            string? prState = data.TryGetProperty("state", out var st) ? st.GetString() : null;
            bool merged = data.TryGetProperty("merged", out var mg) && mg.ValueKind == JsonValueKind.True;

            // Real merge timestamp so the "recently merged" 24h window is accurate
            // even when we self-heal a PR that was merged days ago.
            DateTime? mergedAt = null;
            if (data.TryGetProperty("merged_at", out var ma) && ma.ValueKind == JsonValueKind.String
                && DateTime.TryParse(ma.GetString(), null, DateTimeStyles.AdjustToUniversal, out var parsed))
                mergedAt = parsed;

            return new PullRequestLiveData(pr.PrNumber, pr.RepoFullName, draft, mergeableState, headSha, prState, merged, mergedAt);
        }
        catch
        {
            return new PullRequestLiveData(pr.PrNumber, pr.RepoFullName, null, null, null, null, false, null);
        }
    }

    // ─────────────────────────── Self-healing ───────────────────────────

    /// <summary>
    /// Corrects stale DB rows against live GitHub state. Returns status overrides
    /// so PRs GitHub reports closed/merged never render as "ready".
    /// </summary>
    private async Task<Dictionary<long, string>> SelfHealPrStatesAsync(List<PrRow> prs, IReadOnlyDictionary<(string Repo, long PrNumber), PullRequestLiveData> liveData)
    {
        var overrides = new Dictionary<long, string>();
        bool changed = false;

        foreach (var pr in prs)
        {
            var data = liveData.GetValueOrDefault((pr.RepoFullName, pr.PrNumber));
            if (data == null) continue;

            // GitHub says closed/merged but our DB still has it "open" (missed webhook)
            if (data.State == "closed" && pr.Status == "open")
            {
                var healed = data.Merged ? "merged" : "closed";
                overrides[pr.PrNumber] = healed;
                var entity = await _prs.FindLatestOpenAsync(pr.PrNumber, pr.RepoFullName);
                if (entity != null)
                {
                    entity.Status = healed;
                    // Use the real merge time so the 24h "recently merged" window is
                    // accurate — NOT now (which would resurface old merged PRs).
                    if (data.Merged && data.MergedAt.HasValue) entity.OccurredAt = data.MergedAt.Value;
                    changed = true;
                }
            }
            // Correct OccurredAt for already-merged PRs whose timestamp is wrong
            // (e.g. previously self-healed with now() instead of the real merge time).
            // Update ALL merged rows for this PR to avoid stale duplicates lingering.
            else if (pr.Status == "merged" && data.Merged && data.MergedAt.HasValue)
            {
                var mergedRows = await _prs.GetMergedAsync(pr.PrNumber, pr.RepoFullName);
                foreach (var row in mergedRows)
                {
                    if (Math.Abs((row.OccurredAt - data.MergedAt.Value).TotalMinutes) > 2)
                    {
                        row.OccurredAt = data.MergedAt.Value;
                        changed = true;
                    }
                }
            }
        }

        if (changed) await _uow.SaveChangesAsync();
        return overrides;
    }

    // ─────────────────────────── Review approvals (parallel) ───────────────────────────

    /// <summary>
    /// Checks the GitHub reviews API for each open PR to see if it is APPROVED.
    /// Syncs ReviewApproved in DB and returns overrides for response building.
    /// </summary>
    private async Task<Dictionary<long, bool>> FetchReviewApprovalsAsync(
        List<PrRow> prs,
        Dictionary<long, string> statusOverrides,
        string? token)
    {
        var targets = prs.Where(p => p.Status == "open" && !statusOverrides.ContainsKey(p.PrNumber)).ToList();
        if (targets.Count == 0) return new Dictionary<long, bool>();

        var results = await Task.WhenAll(targets.Select(p => FetchReviewApproval(p.PrNumber, p.RepoFullName, token)));

        var overrides = new Dictionary<long, bool>();
        bool changed = false;
        for (int i = 0; i < targets.Count; i++)
        {
            var approved = results[i];
            if (approved == null) continue;
            overrides[targets[i].PrNumber] = approved.Value;

            var entity = await _prs.FindLatestOpenAsync(targets[i].PrNumber, targets[i].RepoFullName);
            if (entity != null && entity.ReviewApproved != approved.Value)
            {
                entity.ReviewApproved = approved.Value;
                changed = true;
            }
        }

        if (changed) await _uow.SaveChangesAsync();
        return overrides;
    }

    /// <summary>
    /// A PR is approved if any review has state "APPROVED" and no later review
    /// has "CHANGES_REQUESTED" (GitHub uses latest state per reviewer).
    /// Returns null if the API call failed.
    /// </summary>
    private async Task<bool?> FetchReviewApproval(long prNumber, string repoFullName, string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            var response = await _github.GetAsync($"/repos/{repoFullName}/pulls/{prNumber}/reviews?per_page=100", token);
            if (response.StatusCode is < 200 or >= 300 || response.Body is not { } doc)
                return null;

            var latestByReviewer = new Dictionary<string, string>();
            foreach (var review in doc.EnumerateArray())
            {
                var state = review.GetProperty("state").GetString() ?? "";
                var reviewer = review.GetProperty("user").GetProperty("login").GetString() ?? "";
                if (state is "APPROVED" or "CHANGES_REQUESTED" or "DISMISSED")
                    latestByReviewer[reviewer] = state;
            }
            return latestByReviewer.Values.Any(v => v == "APPROVED")
                && !latestByReviewer.Values.Any(v => v == "CHANGES_REQUESTED");
        }
        catch
        {
            return null;
        }
    }

    // ─────────────────────────── Batched DB loads + conclusion ───────────────────────────

    private async Task<List<RunInfo>> LoadRunsAsync(List<string> repos)
    {
        if (repos.Count == 0) return [];
        var raw = await _runs.GetByShasForReposAsync(repos);
        return raw.Select(r => new RunInfo(r.Repo, r.HeadSha, r.WorkflowName, r.Id, r.Status)).ToList();
    }

    private async Task<List<CheckSuiteInfo>> LoadCheckSuitesAsync(HashSet<(string Repo, string Sha)> shaRepoSet)
    {
        if (shaRepoSet.Count == 0) return [];
        var repos = shaRepoSet.Select(s => s.Repo).Distinct().ToList();
        var shas = shaRepoSet.Select(s => s.Sha).Distinct().ToList();
        var raw = await _checkSuites.GetByShasForReposAsync(shas, repos);
        return raw.Select(c => new CheckSuiteInfo(c.RepoFullName, c.HeadSha!, c.Id, c.Conclusion ?? "")).ToList();
    }

    /// <summary>
    /// Determines the conclusion shown on a PR card. Prefers the latest workflow
    /// run status; falls back to the latest CheckSuiteEvent; otherwise the DB value.
    /// </summary>
    private static string? ResolveConclusion(
        string? dbConclusion,
        string? headSha,
        string repo,
        List<RunInfo> allRuns,
        List<CheckSuiteInfo> checkSuites)
    {
        if (headSha == null) return dbConclusion;

        string? conclusion = dbConclusion;
        var latestCheck = checkSuites
            .Where(c => c.Repo == repo && c.HeadSha == headSha)
            .OrderByDescending(c => c.Id)
            .FirstOrDefault();
        if (latestCheck != null)
            conclusion = latestCheck.Conclusion;

        var latestRun = allRuns
            .Where(r => r.Repo == repo && r.HeadSha == headSha
                && r.Status != "superseded" && r.Status != "in_progress")
            .OrderByDescending(r => r.Id)
            .FirstOrDefault();
        if (latestRun != null)
        {
            if (latestRun.Status == "success")
                conclusion = "success";
            else if (latestRun.Status == "failure")
                conclusion = "failure";
            else if (latestRun.Status == "cancelled")
                conclusion = "cancelled";
        }

        return conclusion;
    }
}
