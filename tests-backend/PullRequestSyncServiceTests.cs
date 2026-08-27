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
public class PullRequestSyncServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private readonly FakeGitHubClient _fakeGithub;
    private static int _counter;

    public PullRequestSyncServiceTests(WebApplicationFactory<Program> factory)
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
        var id = Interlocked.Increment(ref _counter) + 6000L;
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

    private static GitHubResponse JsonResponse(int status, object body)
        => new(status, JsonSerializer.SerializeToElement(body));

    private void StubSearch(string username, object items)
        => _fakeGithub.Responses[$"/search/issues?q=type:pr+state:open+author:{username}&per_page=100&page=1"]
            = JsonResponse(200, new { items });

    private void StubRepoPulls(object prs)
        => _fakeGithub.Responses["/repos/acme/repo/pulls?state=open&per_page=100"] = JsonResponse(200, prs);

    private static object SearchItem(long number, string title = "PR #1", bool draft = false, string createdAt = "2026-08-01T10:00:00Z")
        => new
        {
            number,
            title,
            html_url = $"https://github.com/acme/repo/pull/{number}",
            draft,
            created_at = createdAt,
            repository_url = "https://api.github.com/repos/acme/repo"
        };

    private static object RepoPull(long number)
        => new
        {
            number,
            user = new { login = "alice", id = 987654321 },
            head = new { @ref = "feature/x" },
            @base = new { @ref = "main" }
        };

    [Fact]
    public async Task Sync_NoPat_Unauthorized()
    {
        var uid = SeedUser(pat: null);
        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Sync_NewPr_CreatesTrackingRow()
    {
        var uid = SeedUser();
        StubSearch($"user{uid}", new object[] { SearchItem(42, title: "Add feature") });
        StubRepoPulls(new object[] { RepoPull(42) });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(1, body.GetProperty("synced").GetInt32());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pr = Assert.Single(db.PullRequestEvents);
        Assert.Equal(42, pr.PrNumber);
        Assert.Equal("Add feature", pr.Title);
        Assert.Equal("open", pr.Status);
        Assert.Equal("feature/x", pr.HeadBranch);
        Assert.Equal("main", pr.BaseBranch);
        Assert.Equal(987654321L, pr.AuthorGitHubId);
        Assert.Equal(DateTime.Parse("2026-08-01T10:00:00Z").ToUniversalTime(), pr.OccurredAt);
    }

    [Fact]
    public async Task Sync_ExistingPr_UpdatesFields()
    {
        var uid = SeedUser();
        using (var scope = _factory.Services.CreateScope())
        {
            var db0 = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db0.PullRequestEvents.Add(new PullRequestEvent
            {
                PrNumber = 42,
                Title = "Old title",
                AuthorLogin = "alice",
                AuthorGitHubId = 987654321,
                RepoFullName = "acme/repo",
                HeadBranch = "old-branch",
                BaseBranch = "main",
                PrUrl = "https://github.com/acme/repo/pull/42",
                Status = "closed",
                Draft = false,
                OccurredAt = DateTime.UtcNow
            });
            db0.SaveChanges();
        }
        StubSearch($"user{uid}", new object[] { SearchItem(42, title: "New title") });
        StubRepoPulls(new object[] { RepoPull(42) });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var checkScope = _factory.Services.CreateScope();
        var db = checkScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pr = Assert.Single(db.PullRequestEvents);
        Assert.Equal("New title", pr.Title);
        Assert.Equal("open", pr.Status);
        Assert.Equal("feature/x", pr.HeadBranch);
    }

    [Fact]
    public async Task Sync_EmptySearch_NoRows()
    {
        var uid = SeedUser();
        StubSearch($"user{uid}", Array.Empty<object>());

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(0, body.GetProperty("synced").GetInt32());

        using var scope = _factory.Services.CreateScope();
        Assert.Empty(scope.ServiceProvider.GetRequiredService<AppDbContext>().PullRequestEvents);
    }

    [Fact]
    public async Task Sync_SearchError_SyncedZeroOk()
    {
        var uid = SeedUser();
        _fakeGithub.Responses[$"/search/issues?q=type:pr+state:open+author:user{uid}&per_page=100&page=1"]
            = JsonResponse(500, new { message = "boom" });

        var response = await AuthClient(uid).PostAsync("/api/v1/pullrequests/sync", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(0, body.GetProperty("synced").GetInt32());
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
}
