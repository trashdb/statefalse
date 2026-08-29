using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Statefalse.Infrastructure.Data;
using Statefalse.Domain.Models;
using Statefalse.Domain.Contracts;
using Statefalse.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Statefalse.Api.Tests;

[Collection("BackendIntegration")]
public class PullRequestQueryServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private readonly FakeGitHubClient _fakeGithub;
    private static int _counter;

    public PullRequestQueryServiceTests(WebApplicationFactory<Program> factory)
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _fakeGithub = new FakeGitHubClient();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestAuth.Secret);
            builder.UseSetting("Database:Provider", "Sqlite");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(_sqliteConnection));
                services.RemoveAll<IGitHubClient>();
                services.AddScoped<IGitHubClient>(_ => _fakeGithub);
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _sqliteConnection.Close();
        _sqliteConnection.Dispose();
    }

    private HttpClient AuthClient(long gitHubId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            TestAuth.Token(_factory, gitHubId, $"user{gitHubId}"));
        return client;
    }

    private long SeedUser(string pat = "ghp_pat_token")
    {
        var id = Interlocked.Increment(ref _counter) + 2000L;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GitHubUsers.Add(new GitHubUser
        {
            GitHubId = id,
            GitHubUsername = $"user{id}",
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            UserPatToken = pat
        });
        db.SaveChanges();
        return id;
    }

    private void SeedPr(long authorId, int prNumber = 42, string status = "open",
        string repo = "acme/repo", string? subscriberIds = null, DateTime? occurredAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PullRequestEvents.Add(new PullRequestEvent
        {
            PrNumber = prNumber,
            Title = $"PR #{prNumber}",
            AuthorLogin = $"user{authorId}",
            AuthorGitHubId = authorId,
            RepoFullName = repo,
            HeadBranch = "feature/x",
            BaseBranch = "main",
            PrUrl = $"https://github.com/{repo}/pull/{prNumber}",
            Status = status,
            Draft = false,
            OccurredAt = occurredAt ?? DateTime.UtcNow,
            SubscriberIds = subscriberIds
        });
        db.SaveChanges();
    }

    private void SeedWorkflowRun(
        string repo,
        string sha,
        string status,
        string workflowName = "CI",
        long runId = 1,
        DateTime? startedAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.WorkflowRuns.Add(new WorkflowRun
        {
            RunId = runId,
            GitHubId = 2000L,
            WorkflowName = workflowName,
            Repo = repo,
            Actor = "user2000",
            HeadBranch = "feature/x",
            HeadSha = sha,
            Status = status,
            StartedAt = startedAt ?? DateTime.UtcNow
        });
        db.SaveChanges();
    }

    // ───────────── Helpers: GitHub API stubs ─────────────

    private static GitHubResponse JsonResponse(int status, object body)
        => new(status, JsonSerializer.SerializeToElement(body));

    private void StubPull(int prNumber, string sha = "sha1", string? state = "open",
        bool merged = false, string? mergedAt = null, string mergeableState = "clean",
        string repo = "acme/repo")
    {
        _fakeGithub.Responses[$"/repos/{repo}/pulls/{prNumber}"] = JsonResponse(200, new
        {
            draft = false,
            mergeable_state = mergeableState,
            state,
            merged,
            merged_at = mergedAt,
            head = new { sha }
        });
    }

    private void StubCheckRuns(string sha, string repo = "acme/repo")
        => _fakeGithub.Responses[$"/repos/{repo}/commits/{sha}/check-runs?per_page=100"]
            = JsonResponse(200, new { check_runs = Array.Empty<object>() });

    private void StubReviews(int prNumber, params string[] states)
    {
        _fakeGithub.Responses[$"/repos/acme/repo/pulls/{prNumber}/reviews?per_page=100"] = JsonResponse(200,
            states.Select(s => (object)new { state = s, user = new { login = "reviewer" } }).ToArray());
    }

    // ───────────── ciStatus ─────────────

    [Fact]
    public async Task Active_NoRuns_CiStatusWaiting()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubPull(42);
        StubCheckRuns("sha1");

        var prs = await GetActive(uid);

        var pr = Assert.Single(prs);
        Assert.Equal("open", pr.Status);
        Assert.Equal("waiting", pr.CiStatus);
    }

    [Fact]
    public async Task Active_SuccessRun_CiStatusReview()
    {
        var uid = SeedUser();
        SeedPr(uid);
        SeedWorkflowRun("acme/repo", "sha1", "success");
        StubPull(42);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("review", pr.CiStatus);
    }

    [Fact]
    public async Task Active_SuccessRunAndApproved_CiStatusReady()
    {
        var uid = SeedUser();
        SeedPr(uid);
        SeedWorkflowRun("acme/repo", "sha1", "success");
        StubPull(42);
        StubCheckRuns("sha1");
        StubReviews(42, "APPROVED");

        var pr = Assert.Single(await GetActive(uid));
        Assert.True(pr.ReviewApproved);
        Assert.Equal("ready", pr.CiStatus);
    }

    [Fact]
    public async Task Active_FailureRun_CiStatusFailed()
    {
        var uid = SeedUser();
        SeedPr(uid);
        SeedWorkflowRun("acme/repo", "sha1", "failure");
        StubPull(42);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("failed", pr.CiStatus);
    }

    [Fact]
    public async Task Active_InProgressRun_CiStatusWaiting()
    {
        var uid = SeedUser();
        SeedPr(uid);
        SeedWorkflowRun("acme/repo", "sha1", "in_progress");
        StubPull(42);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("waiting", pr.CiStatus);
    }

    [Fact]
    public async Task Active_OlderCancelledInsertedLater_DoesNotOverrideNewerSuccessConclusion()
    {
        var uid = SeedUser();
        SeedPr(uid);
        SeedWorkflowRun("acme/repo", "sha1", "success", runId: 200);
        // Simulate out-of-order persistence: old run is inserted later by a delayed webhook.
        SeedWorkflowRun("acme/repo", "sha1", "cancelled", runId: 100);
        StubPull(42);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("success", pr.Conclusion);
    }

    [Fact]
    public async Task Active_NewerCancelledRun_OverridesOlderFailureConclusion()
    {
        var uid = SeedUser();
        SeedPr(uid);
        SeedWorkflowRun("acme/repo", "sha1", "failure", runId: 100);
        SeedWorkflowRun("acme/repo", "sha1", "cancelled", runId: 200);
        StubPull(42);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("cancelled", pr.Conclusion);
    }

    // ───────────── Self-healing ─────────────

    [Fact]
    public async Task Active_GitHubMerged_HealsDbToMerged()
    {
        var uid = SeedUser();
        SeedPr(uid, status: "open");
        var mergedAt = DateTime.UtcNow.AddDays(-2);
        StubPull(42, state: "closed", merged: true, mergedAt: mergedAt.ToString("O"));
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("merged", pr.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(db.PullRequestEvents);
        Assert.Equal("merged", row.Status);
        Assert.Equal(mergedAt, row.OccurredAt);
    }

    [Fact]
    public async Task Active_GitHubClosedNotMerged_HealsDbToClosed()
    {
        var uid = SeedUser();
        SeedPr(uid, status: "open");
        StubPull(42, state: "closed", merged: false);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("closed", pr.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("closed", Assert.Single(db.PullRequestEvents).Status);
    }

    [Fact]
    public async Task Active_GitHubStillOpen_NoDbChange()
    {
        var uid = SeedUser();
        SeedPr(uid, status: "open");
        StubPull(42, state: "open");
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("open", pr.Status);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("open", Assert.Single(db.PullRequestEvents).Status);
    }

    // ───────────── Review approval ─────────────

    [Fact]
    public async Task Active_ApprovedReview_SyncsReviewApprovedToDb()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubPull(42);
        StubCheckRuns("sha1");
        StubReviews(42, "APPROVED");

        await GetActive(uid);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(Assert.Single(db.PullRequestEvents).ReviewApproved);
    }

    [Fact]
    public async Task Active_ChangesRequested_NotApproved()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubPull(42);
        StubCheckRuns("sha1");
        StubReviews(42, "CHANGES_REQUESTED");

        var pr = Assert.Single(await GetActive(uid));
        Assert.False(pr.ReviewApproved);
    }

    [Fact]
    public async Task Active_NoReviews_NotApproved()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubPull(42);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.False(pr.ReviewApproved);
    }

    [Fact]
    public async Task Active_DuplicateRows_SamePr_Deduplicated()
    {
        var uid = SeedUser();
        SeedPr(uid, prNumber: 42);
        SeedPr(uid, prNumber: 42);
        StubPull(42);
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal(42, pr.PrNumber);
    }

    [Fact]
    public async Task Active_SamePrNumber_DifferentRepos_BothReturned()
    {
        var uid = SeedUser();
        SeedPr(uid, prNumber: 7, repo: "acme/repo");
        SeedPr(uid, prNumber: 7, repo: "other/org");
        StubPull(7, repo: "acme/repo");
        StubPull(7, repo: "other/org");
        StubCheckRuns("sha1");
        StubCheckRuns("sha1", repo: "other/org");

        var prs = await GetActive(uid);
        Assert.Equal(2, prs.Count);
        Assert.Contains(prs, p => p.Repo == "acme/repo");
        Assert.Contains(prs, p => p.Repo == "other/org");
    }

    // ───────────── Subscriber visibility ─────────────

    [Fact]
    public async Task Active_SubscribedUser_SeesOthersPr()
    {
        var author = SeedUser();
        var subscriber = SeedUser();
        SeedPr(author, subscriberIds: $"[{subscriber}]");
        StubPull(42);
        StubCheckRuns("sha1");

        var prs = await GetActive(subscriber);
        var pr = Assert.Single(prs);
        Assert.Equal(author, pr.AuthorGitHubId);
        Assert.True(pr.IsSubscribed);
    }

    [Fact]
    public async Task Active_UnrelatedUser_DoesNotSeePr()
    {
        var author = SeedUser();
        var other = SeedUser();
        SeedPr(author);
        StubPull(42);
        StubCheckRuns("sha1");

        Assert.Empty(await GetActive(other));
    }

    [Fact]
    public async Task Detail_UnrelatedUser_ReturnsNotFoundWithoutProxyingToGitHub()
    {
        var author = SeedUser();
        var other = SeedUser();
        SeedPr(author);

        var response = await AuthClient(other)
            .GetAsync("/api/v1/pullrequests/42/detail?repo=acme/repo");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(0, _fakeGithub.CallCount);
    }

    // ───────────── Merged 24h window ─────────────

    [Fact]
    public async Task Active_MergedRecently_Visible()
    {
        var uid = SeedUser();
        SeedPr(uid, status: "merged", occurredAt: DateTime.UtcNow.AddHours(-1));
        StubPull(42, state: "closed", merged: true, mergedAt: DateTime.UtcNow.AddHours(-1).ToString("O"));
        StubCheckRuns("sha1");

        var pr = Assert.Single(await GetActive(uid));
        Assert.Equal("merged", pr.Status);
    }

    [Fact]
    public async Task Active_MergedOver24hAgo_Hidden()
    {
        var uid = SeedUser();
        SeedPr(uid, status: "merged", occurredAt: DateTime.UtcNow.AddDays(-2));
        StubPull(42, state: "closed", merged: true, mergedAt: DateTime.UtcNow.AddDays(-2).ToString("O"));
        StubCheckRuns("sha1");

        Assert.Empty(await GetActive(uid));
    }

    private async Task<List<PullRequestDto>> GetActive(long gitHubId)
    {
        var response = await AuthClient(gitHubId).GetAsync("/api/v1/pullrequests/active");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<List<PullRequestDto>>())!;
    }

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public Dictionary<string, GitHubResponse> Responses { get; } = new();
        public int CallCount { get; private set; }

        public Task<GitHubResponse> GetAsync(string path, string? token = null, CancellationToken ct = default)
        {
            CallCount++;
            return Task.FromResult(Responses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));
        }

        public Task<GitHubResponse> PostAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));

        public Task<GitHubResponse> PutAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));

        public Task<GitHubResponse> GraphQlAsync(string query, string? token, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));
    }
}
