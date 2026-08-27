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
public class PunishmentServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private static int _counter;

    public PunishmentServiceTests(WebApplicationFactory<Program> factory)
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

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

    private long SeedUser()
    {
        var id = Interlocked.Increment(ref _counter) + 8000L;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.GitHubUsers.Add(new GitHubUser
        {
            GitHubId = id,
            GitHubUsername = $"user{id}",
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        });
        db.SaveChanges();
        return id;
    }

    private void SeedPunishment(string culprit, string repo, string workflow,
        DateTime occurredAt, long runId, bool notified = true)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PunishmentEvents.Add(new PunishmentEvent
        {
            RunId = runId,
            CulpritLogin = culprit,
            RepoFullName = repo,
            WorkflowName = workflow,
            WorkflowUrl = $"https://github.com/{repo}/actions/runs/{runId}",
            OccurredAt = occurredAt,
            WasNotified = notified
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task GetRecent_ReturnsEventsWithinWindow()
    {
        var uid = SeedUser();
        var now = DateTime.UtcNow;
        SeedPunishment("alice", "acme/repo", "CI", now.AddHours(-1), 1);
        SeedPunishment("bob", "acme/repo", "CI", now.AddDays(-10), 2);

        var response = await AuthClient(uid).GetAsync("/api/v1/punishments?days=7");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = (await response.Content.ReadFromJsonAsync<JsonElement>());
        var single = Assert.Single(events.EnumerateArray());
        Assert.Equal(1L, single.GetProperty("runId").GetInt64());
        Assert.Equal("alice", single.GetProperty("culpritLogin").GetString());
    }

    [Fact]
    public async Task GetRecent_RespectsLimit()
    {
        var uid = SeedUser();
        var now = DateTime.UtcNow;
        SeedPunishment("alice", "acme/repo", "CI", now.AddHours(-1), 1);
        SeedPunishment("bob", "acme/repo", "CI", now.AddHours(-2), 2);
        SeedPunishment("carol", "acme/repo", "CI", now.AddHours(-3), 3);

        var response = await AuthClient(uid).GetAsync("/api/v1/punishments?days=7&limit=2");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(2, events.GetArrayLength());
    }

    [Fact]
    public async Task GetRecent_NoEvents_EmptyList()
    {
        var uid = SeedUser();
        var response = await AuthClient(uid).GetAsync("/api/v1/punishments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var events = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Empty(events.EnumerateArray());
    }

    [Fact]
    public async Task GetSummary_RanksCulpritsWorkflowsRepos()
    {
        var uid = SeedUser();
        var now = DateTime.UtcNow;
        SeedPunishment("alice", "acme/repo", "CI", now.AddHours(-1), 1);
        SeedPunishment("alice", "acme/repo", "CI", now.AddHours(-2), 2);
        SeedPunishment("alice", "other/repo", "Tests", now.AddHours(-3), 3);
        SeedPunishment("bob", "acme/repo", "CI", now.AddHours(-4), 4);

        var response = await AuthClient(uid).GetAsync("/api/v1/punishments/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());

        var culprits = body.GetProperty("topCulprits").EnumerateArray().ToList();
        Assert.Equal(2, culprits.Count);
        Assert.Equal("alice", culprits[0].GetProperty("login").GetString());
        Assert.Equal(3, culprits[0].GetProperty("count").GetInt32());

        var workflows = body.GetProperty("topWorkflows").EnumerateArray().ToList();
        Assert.Equal(2, workflows.Count);
        Assert.Equal("CI", workflows[0].GetProperty("name").GetString());
        Assert.Equal("acme/repo", workflows[0].GetProperty("repo").GetString());
        Assert.Equal(3, workflows[0].GetProperty("count").GetInt32());

        var repos = body.GetProperty("topRepos").EnumerateArray().ToList();
        Assert.Equal(2, repos.Count);
        Assert.Equal("acme/repo", repos[0].GetProperty("fullName").GetString());
        Assert.Equal(3, repos[0].GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task GetSummary_TakesTopFive()
    {
        var uid = SeedUser();
        var now = DateTime.UtcNow;
        for (var i = 1; i <= 7; i++)
            SeedPunishment($"culprit{i}", "acme/repo", "CI", now.AddHours(-i), i);

        var response = await AuthClient(uid).GetAsync("/api/v1/punishments/summary");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(5, body.GetProperty("topCulprits").GetArrayLength());
        Assert.Equal(1, body.GetProperty("topRepos").GetArrayLength());
    }
}
