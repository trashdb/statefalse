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
public class GitHubApiServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private readonly FakeGitHubClient _fakeGithub;
    private static int _counter;

    public GitHubApiServiceTests(WebApplicationFactory<Program> factory)
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

    private long SeedUser(string? username = null, string? pat = "ghp_pat_token")
    {
        var id = Interlocked.Increment(ref _counter) + 5000L;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GitHubUsers.Add(new GitHubUser
        {
            GitHubId = id,
            GitHubUsername = username ?? $"user{id}",
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            UserPatToken = pat
        });
        db.SaveChanges();
        return id;
    }

    private static GitHubResponse JsonResponse(int status, object body)
        => new(status, JsonSerializer.SerializeToElement(body));

    // ───────────── Create PR ─────────────

    private void StubCreatePr(object body, int status = 201)
        => _fakeGithub.PostResponses["/repos/acme/repo/pulls"] = JsonResponse(status, body);

    [Fact]
    public async Task CreatePr_NoPat_Unauthorized()
    {
        var uid = SeedUser(pat: null);
        var response = await AuthClient(uid)
            .PostAsync("/api/v1/github/create-pr?repo=acme/repo&head=feature/x&baseBranch=main&title=Hi", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreatePr_Success_SyncsDbAndResolvesSubscribers()
    {
        var uid = SeedUser();
        var sub1 = SeedUser(username: "subone");
        var sub2 = SeedUser(username: "subtwo");
        StubCreatePr(new
        {
            number = 42,
            html_url = "https://github.com/acme/repo/pull/42",
            title = "Add feature"
        });

        var response = await AuthClient(uid).PostAsync(
            "/api/v1/github/create-pr?repo=acme/repo&head=feature/x&baseBranch=main&title=Add%20feature&subscribers=subone,subtwo", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(42L, body.GetProperty("prNumber").GetInt64());
        Assert.Contains("acme/repo/pull/42", body.GetProperty("url").GetString());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pr = Assert.Single(db.PullRequestEvents);
        Assert.Equal(42, pr.PrNumber);
        Assert.Equal("Add feature", pr.Title);
        Assert.Equal(uid, pr.AuthorGitHubId);
        Assert.Equal("open", pr.Status);
        Assert.Contains(sub1.ToString(), pr.SubscriberIds!);
        Assert.Contains(sub2.ToString(), pr.SubscriberIds!);
    }

    [Fact]
    public async Task CreatePr_AlreadyExists_ReturnsExisting()
    {
        var uid = SeedUser();
        StubCreatePr(new
        {
            message = "Validation Failed",
            errors = new[] { new { message = "A pull request already exists for user:feature/x." } }
        }, status: 422);
        _fakeGithub.GetResponses["/repos/acme/repo/pulls?state=open&per_page=100"] = JsonResponse(200, new object[]
        {
            new { number = 7, html_url = "https://github.com/acme/repo/pull/7", head = new { @ref = "feature/x" } }
        });

        var response = await AuthClient(uid).PostAsync(
            "/api/v1/github/create-pr?repo=acme/repo&head=feature/x&baseBranch=main&title=Hi", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(7L, body.GetProperty("prNumber").GetInt64());
        Assert.True(body.GetProperty("existing").GetBoolean());

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(db.PullRequestEvents);
    }

    [Fact]
    public async Task CreatePr_ApiError_ReturnsMessage()
    {
        var uid = SeedUser();
        StubCreatePr(new { message = "Head branch was not found" }, status: 422);

        var response = await AuthClient(uid).PostAsync(
            "/api/v1/github/create-pr?repo=acme/repo&head=nope&baseBranch=main&title=Hi", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal("Head branch was not found", body.GetProperty("error").GetString());
    }

    [Fact]
    public async Task CreatePr_GithubUnreachable_BadGateway()
    {
        var uid = SeedUser();
        StubCreatePr(new { }, status: 0);

        var response = await AuthClient(uid).PostAsync(
            "/api/v1/github/create-pr?repo=acme/repo&head=feature/x&baseBranch=main&title=Hi", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    // ───────────── My branches ─────────────

    [Fact]
    public async Task MyBranches_ReturnsOwnBranchesSkipsDependabot()
    {
        var uid = SeedUser();
        _fakeGithub.GetResponses["/repos/acme/repo/branches?per_page=100"] = JsonResponse(200, new object[]
        {
            new { name = "feature/x" },
            new { name = "dependabot/npm-update" },
            new { name = "other-branch" }
        });
        _fakeGithub.GetResponses["/repos/acme/repo/branches/feature/x"] = JsonResponse(200, new
        {
            commit = new { author = new { login = $"user{uid}" } }
        });
        _fakeGithub.GetResponses["/repos/acme/repo/branches/other-branch"] = JsonResponse(200, new
        {
            commit = new { author = new { login = "someoneelse" } }
        });

        var response = await AuthClient(uid).GetAsync("/api/v1/github/my-branches?repo=acme/repo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var branches = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, branches.GetArrayLength());
        Assert.Equal("feature/x", branches[0].GetProperty("name").GetString());
        Assert.False(_fakeGithub.GetResponses.ContainsKey("/repos/acme/repo/branches/dependabot/npm-update")
            && _fakeGithub.CalledDetail.Contains("/repos/acme/repo/branches/dependabot/npm-update"));
    }

    [Fact]
    public async Task MyBranches_NoPat_Unauthorized()
    {
        var uid = SeedUser(pat: null);
        var response = await AuthClient(uid).GetAsync("/api/v1/github/my-branches?repo=acme/repo");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MyBranches_GithubUnreachable_BadGateway()
    {
        var uid = SeedUser();
        _fakeGithub.GetResponses["/repos/acme/repo/branches?per_page=100"] = new GitHubResponse(0, null);

        var response = await AuthClient(uid).GetAsync("/api/v1/github/my-branches?repo=acme/repo");
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);
    }

    private sealed class FakeGitHubClient : IGitHubClient
    {
        public Dictionary<string, GitHubResponse> GetResponses { get; } = new();
        public Dictionary<string, GitHubResponse> PostResponses { get; } = new();
        public HashSet<string> CalledDetail { get; } = new();

        public Task<GitHubResponse> GetAsync(string path, string? token = null, CancellationToken ct = default)
        {
            if (path.StartsWith("/repos/acme/repo/branches/")) CalledDetail.Add(path);
            return Task.FromResult(GetResponses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));
        }

        public Task<GitHubResponse> PostAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(PostResponses.TryGetValue(path, out var r) ? r : new GitHubResponse(404, null));

        public Task<GitHubResponse> PutAsync(string path, string? token, object? body = null, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));

        public Task<GitHubResponse> GraphQlAsync(string query, string? token, CancellationToken ct = default)
            => Task.FromResult(new GitHubResponse(404, null));
    }
}
