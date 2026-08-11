using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Statefalse.Infrastructure.Data;
using Statefalse.Domain.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Statefalse.Api.Tests;

[Collection("BackendIntegration")]
public class PullRequestSubscriptionServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private static int _counter;

    public PullRequestSubscriptionServiceTests(WebApplicationFactory<Program> factory)
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestAuth.Secret);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(_sqliteConnection));
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

    private long SeedUser(string? username = null)
    {
        var id = Interlocked.Increment(ref _counter) + 4000L;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GitHubUsers.Add(new GitHubUser
        {
            GitHubId = id,
            GitHubUsername = username ?? $"user{id}",
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow,
            AvatarUrl = $"https://avatars.example/{id}.png"
        });
        db.SaveChanges();
        return id;
    }

    private void SeedPr(long authorId, string? subscriberIds = null, int prNumber = 42, string status = "open")
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
            HeadBranch = "feature/x",
            BaseBranch = "main",
            PrUrl = $"https://github.com/acme/repo/pull/{prNumber}",
            Status = status,
            Draft = false,
            OccurredAt = DateTime.UtcNow,
            SubscriberIds = subscriberIds
        });
        db.SaveChanges();
    }

    private string[] DbSubscriberIds(int prNumber = 42)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var raw = Assert.Single(db.PullRequestEvents.Where(p => p.PrNumber == prNumber)).SubscriberIds;
        return string.IsNullOrEmpty(raw) ? Array.Empty<string>() : raw.Trim('[', ']').Split(',').Where(s => s.Length > 0).ToArray();
    }

    // ───────────── Subscribe / Unsubscribe ─────────────

    [Fact]
    public async Task Subscribe_AddsGitHubId()
    {
        var author = SeedUser();
        var user = SeedUser();
        SeedPr(author);

        var response = await AuthClient(user).PostAsync("/api/pullrequests/42/subscribe?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains(user.ToString(), DbSubscriberIds());
    }

    [Fact]
    public async Task Subscribe_Twice_IsIdempotent()
    {
        var author = SeedUser();
        var user = SeedUser();
        SeedPr(author);

        await AuthClient(user).PostAsync("/api/pullrequests/42/subscribe?repo=acme/repo", null);
        await AuthClient(user).PostAsync("/api/pullrequests/42/subscribe?repo=acme/repo", null);

        Assert.Single(DbSubscriberIds());
    }

    [Fact]
    public async Task Subscribe_UntrackedPr_NotFound()
    {
        var user = SeedUser();
        var response = await AuthClient(user).PostAsync("/api/pullrequests/42/subscribe?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unsubscribe_RemovesGitHubId()
    {
        var author = SeedUser();
        var user = SeedUser();
        SeedPr(author, subscriberIds: $"[{user}]");

        var response = await AuthClient(user).PostAsync("/api/pullrequests/42/unsubscribe?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Empty(DbSubscriberIds());
    }

    [Fact]
    public async Task Unsubscribe_NotSubscribed_StillOk()
    {
        var author = SeedUser();
        var user = SeedUser();
        SeedPr(author);

        var response = await AuthClient(user).PostAsync("/api/pullrequests/42/unsubscribe?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.False(body.GetProperty("subscribed").GetBoolean());
    }

    // ───────────── Get subscribers ─────────────

    [Fact]
    public async Task GetSubscribers_ReturnsUsers()
    {
        var author = SeedUser();
        var sub1 = SeedUser(username: "subone");
        var sub2 = SeedUser(username: "subtwo");
        SeedPr(author, subscriberIds: $"[{sub1},{sub2}]");

        var response = await AuthClient(author).GetAsync("/api/pullrequests/42/subscribers?repo=acme/repo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(2, body.GetProperty("subscribers").GetArrayLength());
        var usernames = body.GetProperty("subscribers")
            .EnumerateArray().Select(s => s.GetProperty("gitHubUsername").GetString()).ToArray();
        Assert.Contains("subone", usernames);
        Assert.Contains("subtwo", usernames);
    }

    [Fact]
    public async Task GetSubscribers_NoSubscribers_EmptyList()
    {
        var author = SeedUser();
        SeedPr(author);

        var response = await AuthClient(author).GetAsync("/api/pullrequests/42/subscribers?repo=acme/repo");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Empty(body.GetProperty("subscribers").EnumerateArray());
    }

    [Fact]
    public async Task GetSubscribers_UntrackedPr_NotFound()
    {
        var user = SeedUser();
        var response = await AuthClient(user).GetAsync("/api/pullrequests/42/subscribers?repo=acme/repo");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ───────────── Add / remove subscriber ─────────────

    [Fact]
    public async Task AddSubscriber_ByUsername_AuthorAllowed()
    {
        var author = SeedUser();
        var target = SeedUser(username: "targetuser");
        SeedPr(author);

        var response = await AuthClient(author)
            .PostAsync("/api/pullrequests/42/add-subscriber?repo=acme/repo&username=targetuser", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains(target.ToString(), DbSubscriberIds());
    }

    [Fact]
    public async Task AddSubscriber_BySubscriberId_AuthorAllowed()
    {
        var author = SeedUser();
        var target = SeedUser();
        SeedPr(author);

        var response = await AuthClient(author)
            .PostAsync($"/api/pullrequests/42/add-subscriber?repo=acme/repo&subscriberId={target}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.Contains(target.ToString(), DbSubscriberIds());
    }

    [Fact]
    public async Task AddSubscriber_NonAuthor_Forbidden()
    {
        var author = SeedUser();
        var other = SeedUser();
        var target = SeedUser();
        SeedPr(author);

        var response = await AuthClient(other)
            .PostAsync($"/api/pullrequests/42/add-subscriber?repo=acme/repo&subscriberId={target}", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(DbSubscriberIds());
    }

    [Fact]
    public async Task AddSubscriber_UnknownUsername_NotFound()
    {
        var author = SeedUser();
        SeedPr(author);

        var response = await AuthClient(author)
            .PostAsync("/api/pullrequests/42/add-subscriber?repo=acme/repo&username=nobody", null);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddSubscriber_NoTarget_BadRequest()
    {
        var author = SeedUser();
        SeedPr(author);

        var response = await AuthClient(author)
            .PostAsync("/api/pullrequests/42/add-subscriber?repo=acme/repo", null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RemoveSubscriber_AuthorRemovesOther()
    {
        var author = SeedUser();
        var target = SeedUser();
        SeedPr(author, subscriberIds: $"[{target}]");

        var response = await AuthClient(author)
            .PostAsync($"/api/pullrequests/42/remove-subscriber?repo=acme/repo&subscriberId={target}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(DbSubscriberIds());
    }

    [Fact]
    public async Task RemoveSubscriber_SelfUnsubscribe_Allowed()
    {
        var author = SeedUser();
        var user = SeedUser();
        SeedPr(author, subscriberIds: $"[{user}]");

        var response = await AuthClient(user)
            .PostAsync($"/api/pullrequests/42/remove-subscriber?repo=acme/repo&subscriberId={user}", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(DbSubscriberIds());
    }

    [Fact]
    public async Task RemoveSubscriber_NonAuthorRemovingOther_Forbidden()
    {
        var author = SeedUser();
        var other = SeedUser();
        var target = SeedUser();
        SeedPr(author, subscriberIds: $"[{target}]");

        var response = await AuthClient(other)
            .PostAsync($"/api/pullrequests/42/remove-subscriber?repo=acme/repo&subscriberId={target}", null);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Contains(target.ToString(), DbSubscriberIds());
    }
}
