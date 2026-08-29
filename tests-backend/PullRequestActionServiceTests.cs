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
public class PullRequestActionServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private readonly FakeGitHubClient _fakeGithub;
    private static int _counter;

    public PullRequestActionServiceTests(WebApplicationFactory<Program> factory)
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

    private long SeedUser(string? pat = "ghp_pat_token")
    {
        var id = Interlocked.Increment(ref _counter) + 3000L;
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
        string headBranch = "feature/x", bool draft = false)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PullRequestEvents.Add(new PullRequestEvent
        {
            PrNumber = prNumber,
            Title = $"PR #{prNumber}",
            AuthorLogin = $"user{authorId}",
            AuthorGitHubId = authorId,
            RepoFullName = "acme/repo",
            HeadBranch = headBranch,
            BaseBranch = "main",
            PrUrl = $"https://github.com/acme/repo/pull/{prNumber}",
            Status = status,
            Draft = draft,
            OccurredAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private void SeedWorkflowRun(string repo, string branch, string status, long runId = 1)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.WorkflowRuns.Add(new WorkflowRun
        {
            RunId = runId,
            GitHubId = 3000L,
            WorkflowName = "CI",
            Repo = repo,
            Actor = "user3000",
            HeadBranch = branch,
            HeadSha = "sha1",
            Status = status,
            StartedAt = DateTime.UtcNow
        });
        db.SaveChanges();
    }

    private static GitHubResponse JsonResponse(int status, object body)
        => new(status, JsonSerializer.SerializeToElement(body));

    // ───────────── Merge ─────────────

    private void StubMergePr(string sha = "sha1", string title = "PR #42")
        => _fakeGithub.Responses["/repos/acme/repo/pulls/42"] = JsonResponse(200, new
        {
            head = new { sha },
            title,
            node_id = "PR_kwDOabc"
        });

    private void StubMergeResult(object body, int status = 200)
        => _fakeGithub.Responses["/repos/acme/repo/pulls/42/merge"] = JsonResponse(status, body);

    [Fact]
    public async Task Merge_NoPat_Unauthorized()
    {
        var uid = SeedUser(pat: null);
        SeedPr(uid);
        StubMergePr();
        StubMergeResult(new { merged = true });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/merge?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Merge_Success_MarksPrMergedAndReturnsSha()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubMergePr();
        StubMergeResult(new { merged = true, sha = "abc123", message = "Pull Request successfully merged" });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/merge?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.True(body.GetProperty("merged").GetBoolean());
        Assert.Equal("abc123", body.GetProperty("sha").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("merged", Assert.Single(db.PullRequestEvents).Status);
    }

    [Fact]
    public async Task Merge_ApiError_ReturnsErrorStatusAndMessage()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubMergePr();
        StubMergeResult(new { message = "Pull Request is not mergeable" }, status: 405);

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/merge?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("Pull Request is not mergeable", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Merge_GithubUnreachable_BadGateway()
    {
        var uid = SeedUser();
        SeedPr(uid);
        _fakeGithub.Responses["/repos/acme/repo/pulls/42"] = new GitHubResponse(0, null);

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/merge?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    [Fact]
    public async Task Merge_UnrelatedUser_ReturnsNotFoundWithoutCallingGitHub()
    {
        var author = SeedUser();
        var other = SeedUser();
        SeedPr(author);

        var response = await AuthClient(other)
            .PostAsync("/api/v1/pullrequests/42/merge?repo=acme/repo", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(_fakeGithub.Responses);
    }

    // ───────────── Draft ─────────────

    [Fact]
    public async Task Draft_NoPat_Unauthorized()
    {
        var uid = SeedUser(pat: null);
        SeedPr(uid);

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/draft?repo=acme/repo&draft=true", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Draft_SetDraft_SucceedsAndUpdatesDb()
    {
        var uid = SeedUser();
        SeedPr(uid, draft: false);
        StubMergePr();
        _fakeGithub.GraphQlResponse = JsonResponse(200, new
        {
            data = new
            {
                convertPullRequestToDraft = new { pullRequest = new { id = "PR_x", isDraft = true } }
            }
        });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/draft?repo=acme/repo&draft=true", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.True(body.GetProperty("draft").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(Assert.Single(db.PullRequestEvents).Draft);
    }

    [Fact]
    public async Task Draft_ReadyForReview_SetsDraftFalseInDb()
    {
        var uid = SeedUser();
        SeedPr(uid, draft: true);
        StubMergePr();
        _fakeGithub.GraphQlResponse = JsonResponse(200, new
        {
            data = new
            {
                markPullRequestReadyForReview = new { pullRequest = new { id = "PR_x", isDraft = false } }
            }
        });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/draft?repo=acme/repo&draft=false", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(Assert.Single(db.PullRequestEvents).Draft);
    }

    [Fact]
    public async Task Draft_GraphQlError_Returns422()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubMergePr();
        _fakeGithub.GraphQlResponse = JsonResponse(200, new
        {
            errors = new[] { new { message = "Can only convert a pull request that is open to draft." } }
        });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/draft?repo=acme/repo&draft=true", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // ───────────── Update branch ─────────────

    private void StubUpdateBranch(object body, int status = 200)
        => _fakeGithub.Responses["/repos/acme/repo/pulls/42/update-branch"] = JsonResponse(status, body);

    [Fact]
    public async Task UpdateBranch_Success_SupersedesStaleRuns()
    {
        var uid = SeedUser();
        SeedPr(uid, headBranch: "feature/x");
        SeedWorkflowRun("acme/repo", "feature/x", "in_progress", runId: 1);
        SeedWorkflowRun("acme/repo", "feature/x", "failure", runId: 2);
        StubUpdateBranch(new { message = "Branch updated" });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/update-branch?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, db.WorkflowRuns.Count(w => w.Status == "superseded"));
    }

    [Fact]
    public async Task UpdateBranch_UntrackedPr_ReturnsNotFound()
    {
        var uid = SeedUser();
        StubUpdateBranch(new { message = "Branch updated" });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/update-branch?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateBranch_GitHubError_ReturnsMessage()
    {
        var uid = SeedUser();
        SeedPr(uid);
        StubUpdateBranch(new { message = "Branch protection rule prevented update" }, status: 403);

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/42/update-branch?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("Branch protection rule prevented update", body.GetProperty("error").GetString());
    }

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public Dictionary<string, GitHubResponse> Responses { get; } = new();
        public GitHubResponse? GraphQlResponse { get; set; }

        public Task<GitHubResponse> GetAsync(string path, string? token = null, CancellationToken ct = default)
            => Task.FromResult(Responses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));

        public Task<GitHubResponse> PostAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));

        public Task<GitHubResponse> PutAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(Responses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));

        public Task<GitHubResponse> GraphQlAsync(string query, string? token, CancellationToken ct = default)
            => Task.FromResult(GraphQlResponse ?? new GitHubResponse(404, null));
    }
}
