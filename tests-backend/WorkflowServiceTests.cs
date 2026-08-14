using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Statefalse.Infrastructure.Data;
using Statefalse.Domain.Models;
using Statefalse.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Statefalse.Api.Tests;

[Collection("BackendIntegration")]
public class WorkflowServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private readonly FakeGitHubClient _fakeGithub;
    private static int _counter;

    public WorkflowServiceTests(WebApplicationFactory<Program> factory)
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _fakeGithub = new FakeGitHubClient();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestAuth.Secret);
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

    private long SeedUser(string? pat = "ghp_pat_token")
    {
        var id = Interlocked.Increment(ref _counter) + 7000L;
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

    private void SeedPr(long authorId, string repo = "acme/repo", string headBranch = "feature/x", int prNumber = 42)
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
            HeadBranch = headBranch,
            BaseBranch = "main",
            PrUrl = $"https://github.com/{repo}/pull/{prNumber}",
            Status = "open",
            Draft = false,
            OccurredAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private long SeedRun(long gitHubId, long runId = 1, string status = "success",
        string repo = "acme/repo", string branch = "feature/x", string? targetIds = null, string workflowName = "CI")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var run = new WorkflowRun
        {
            RunId = runId,
            GitHubId = gitHubId,
            WorkflowName = workflowName,
            Repo = repo,
            Actor = "actor",
            HeadBranch = branch,
            Trigger = "push",
            Status = status,
            StartedAt = DateTime.UtcNow,
            TargetGitHubIds = targetIds
        };
        db.WorkflowRuns.Add(run);
        db.SaveChanges();
        return run.Id;
    }

    private static GitHubResponse JsonResponse(int status, object body)
        => new(status, JsonSerializer.SerializeToElement(body));

    // ───────────── Get runs ─────────────

    [Fact]
    public async Task GetRuns_ReturnsOwnRuns()
    {
        var uid = SeedUser();
        SeedRun(uid, runId: 1);
        SeedRun(uid, runId: 2);
        SeedPr(uid);

        var response = await AuthClient(uid).GetAsync("/api/v1/workflows/runs?limit=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var runs = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(2, runs.GetArrayLength());
        var runIds = runs.EnumerateArray().Select(r => r.GetProperty("runId").GetInt64()).ToArray();
        Assert.Contains(1L, runIds);
        Assert.Contains(2L, runIds);
    }

    [Fact]
    public async Task GetRuns_OtherUsersRuns_Hidden()
    {
        var uid = SeedUser();
        var other = SeedUser();
        SeedRun(other, runId: 1);

        var response = await AuthClient(uid).GetAsync("/api/v1/workflows/runs?limit=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var runs = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Empty(runs.EnumerateArray());
    }

    // ───────────── Set target ─────────────

    private static StringContent TargetBody(params long[] ids)
        => new(JsonSerializer.Serialize(new { targetGitHubIds = ids }), System.Text.Encoding.UTF8, "application/json");

    [Fact]
    public async Task SetTarget_NotFound_404()
    {
        var uid = SeedUser();
        var response = await AuthClient(uid).PutAsync("/api/v1/workflows/runs/999/target", TargetBody(1, 2));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SetTarget_Owner_UpdatesTargets()
    {
        var uid = SeedUser();
        SeedRun(uid, runId: 1);

        var response = await AuthClient(uid).PutAsync("/api/v1/workflows/runs/1/target", TargetBody(111, 222));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("[111,222]", Assert.Single(db.WorkflowRuns).TargetGitHubIds);
    }

    [Fact]
    public async Task SetTarget_NonOwnerNotTarget_Forbidden()
    {
        var other = SeedUser();
        var uid = SeedUser();
        SeedRun(other, runId: 1);
        SeedPr(uid, headBranch: "other-branch");

        var response = await AuthClient(uid).PutAsync("/api/v1/workflows/runs/1/target", TargetBody(111));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ───────────── Rerun ─────────────

    [Fact]
    public async Task Rerun_NotFound_404()
    {
        var uid = SeedUser();
        var response = await AuthClient(uid).PostAsync("/api/v1/workflows/runs/999/rerun", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Rerun_NoPat_Unauthorized()
    {
        var uid = SeedUser(pat: null);
        SeedRun(uid, runId: 1);

        var response = await AuthClient(uid).PostAsync("/api/v1/workflows/runs/1/rerun", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Rerun_Success_CreatesInProgressRun()
    {
        var uid = SeedUser();
        SeedRun(uid, runId: 1);
        _fakeGithub.Responses["/repos/acme/repo/actions/runs/1/rerun"] = JsonResponse(204, null!);

        var response = await AuthClient(uid).PostAsync("/api/v1/workflows/runs/1/rerun", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.True(body.GetProperty("rerun").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runs = db.WorkflowRuns.ToList();
        Assert.Equal(2, runs.Count);
        var newRun = runs.Single(r => r.Trigger == "workflow_dispatch");
        Assert.Equal("in_progress", newRun.Status);
        Assert.Equal(uid, newRun.GitHubId);
        Assert.Equal("CI", newRun.WorkflowName);
    }

    [Fact]
    public async Task Rerun_GithubError_StatusPassthrough()
    {
        var uid = SeedUser();
        SeedRun(uid, runId: 1);
        _fakeGithub.Responses["/repos/acme/repo/actions/runs/1/rerun"] = JsonResponse(409, new { message = "conflict" });

        var response = await AuthClient(uid).PostAsync("/api/v1/workflows/runs/1/rerun", null);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ───────────── Sync active ─────────────

    [Fact]
    public async Task SyncActive_NoPat_Unauthorized()
    {
        var uid = SeedUser(pat: null);
        var response = await AuthClient(uid).PostAsync("/api/v1/workflows/sync-active", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SyncActive_NoPrs_ZeroSynced()
    {
        var uid = SeedUser();
        var response = await AuthClient(uid).PostAsync("/api/v1/workflows/sync-active", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(0, body.GetProperty("synced").GetInt32());
        Assert.Equal(0, body.GetProperty("repos").GetInt32());
    }

    [Fact]
    public async Task SyncActive_CreatesMissingRunsAndSkipsExisting()
    {
        var uid = SeedUser();
        SeedPr(uid);
        _fakeGithub.Responses["/repos/acme/repo/actions/runs?status=in_progress&per_page=10"] = JsonResponse(200, new
        {
            workflow_runs = new object[]
            {
                new { id = 10L, name = "CI", actor = new { login = "actor1" }, head_branch = "feature/x", html_url = "https://github.com/acme/repo/actions/runs/10", run_started_at = "2026-08-01T10:00:00Z", @event = "push" },
                new { id = 11L, name = "Dependency Review", actor = new { login = "actor2" }, head_branch = "feature/x", html_url = "https://github.com/acme/repo/actions/runs/11", run_started_at = "2026-08-01T10:00:00Z", @event = "pull_request" }
            }
        });
        SeedRun(uid, runId: 10, status: "in_progress");

        var response = await AuthClient(uid).PostAsync("/api/v1/workflows/sync-active", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(1, body.GetProperty("synced").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var runs = db.WorkflowRuns.ToList();
        Assert.Equal(2, runs.Count);
        var created = runs.Single(r => r.RunId == 11);
        Assert.Equal("in_progress", created.Status);
        Assert.Equal("feature/x", created.HeadBranch);
        Assert.True(created.IsIgnored);
        var existing = runs.Single(r => r.RunId == 10);
        Assert.Equal(1, db.WorkflowRuns.Count(r => r.RunId == 10));
    }

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public Dictionary<string, GitHubResponse> Responses { get; } = new();

        public Task<GitHubResponse> GetAsync(string path, string? token = null, CancellationToken ct = default)
            => Task.FromResult(Responses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));

        public Task<GitHubResponse> PostAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(Responses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));

        public Task<GitHubResponse> PutAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));

        public Task<GitHubResponse> GraphQlAsync(string query, string? token, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));
    }
}
