using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
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
public class WebhookHandlersTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string WebhookSecret = "test-secret";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;

    public WebhookHandlersTests(WebApplicationFactory<Program> factory)
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestAuth.Secret);
            builder.UseSetting("WebhookSecret", WebhookSecret);
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

    private T Query<T>(Func<AppDbContext, T> fn)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return fn(db);
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(string eventType, string payload)
    {
        var client = _factory.CreateClient();
        var content = new StringContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add("X-GitHub-Event", eventType);
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes(payload));
        content.Headers.Add("X-Hub-Signature-256", "sha256=" + Convert.ToHexString(hash).ToLowerInvariant());
        return await client.PostAsync("/api/webhook/github", content);
    }

    private static string WorkflowRunPayload(
        long runId,
        string action,
        string? conclusion = null,
        string workflow = "CI",
        string branch = "feature/x",
        string actor = "alice")
    {
        var conclusionJson = conclusion is null ? "" : $",\"conclusion\":\"{conclusion}\"";
        return $$"""
        {
          "action": "{{action}}",
          "workflow_run": {
            "id": {{runId}},
            "name": "{{workflow}}",
            "head_branch": "{{branch}}",
            "head_sha": "deadbeef",
            "run_started_at": "2026-08-10T08:00:00Z",
            "html_url": "https://github.com/acme/repo/actions/runs/{{runId}}",
            "event": "push",
            "pull_requests": [ { "number": 42, "merged_by": { "id": 987654321, "login": "{{actor}}" }, "user": { "id": 111222333, "login": "{{actor}}" } } ]{{conclusionJson}}
          },
          "repository": { "full_name": "acme/repo" },
          "sender": { "id": 987654321, "login": "{{actor}}" }
        }
        """;
    }

    // ───────────── workflow_run: in_progress ─────────────

    [Fact]
    public async Task InProgress_CreatesWorkflowRun()
    {
        var response = await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "in_progress"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var runs = Query(db => db.WorkflowRuns.ToList());
        var run = Assert.Single(runs);
        Assert.Equal(1L, run.RunId);
        Assert.Equal("in_progress", run.Status);
        Assert.Equal("alice", run.Actor);
        Assert.Equal(987654321L, run.GitHubId);
        Assert.Equal("acme/repo", run.Repo);
        Assert.Equal("CI", run.WorkflowName);
        Assert.Equal("feature/x", run.HeadBranch);
        Assert.False(run.IsIgnored);
    }

    [Fact]
    public async Task InProgress_DuplicateEvent_DoesNotCreateDuplicateRow()
    {
        var payload = WorkflowRunPayload(1, "in_progress");
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", payload)).StatusCode);

        var count = Query(db => db.WorkflowRuns.Count());
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task InProgress_NewRunOnSameBranch_SupersedesPrevious()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "in_progress", branch: "main"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(2, "in_progress", branch: "main"))).StatusCode);

        var runs = Query(db => db.WorkflowRuns.OrderBy(w => w.RunId).ToList());
        Assert.Equal(2, runs.Count);
        Assert.Equal("superseded", runs[0].Status);
        Assert.Equal("in_progress", runs[1].Status);
    }

    [Fact]
    public async Task InProgress_NewRunOnDifferentBranch_DoesNotSupersede()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "in_progress", branch: "main"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(2, "in_progress", branch: "other"))).StatusCode);

        var runs = Query(db => db.WorkflowRuns.OrderBy(w => w.RunId).ToList());
        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal("in_progress", r.Status));
    }

    [Fact]
    public async Task InProgress_IgnoredWorkflow_SetsIsIgnored()
    {
        var response = await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "in_progress", workflow: "CodeQL High Severity"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var run = Assert.Single(Query(db => db.WorkflowRuns.ToList()));
        Assert.True(run.IsIgnored);
    }

    // ───────────── workflow_run: completed ─────────────

    [Fact]
    public async Task Completed_Success_UpdatesInProgressRun_NoPunishment()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "in_progress"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "completed", conclusion: "success"))).StatusCode);

        var run = Assert.Single(Query(db => db.WorkflowRuns.ToList()));
        Assert.Equal("success", run.Status);
        Assert.Empty(Query(db => db.PunishmentEvents.ToList()));
    }

    [Fact]
    public async Task Completed_Failure_CreatesPunishmentEvent()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "in_progress"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "completed", conclusion: "failure"))).StatusCode);

        var run = Assert.Single(Query(db => db.WorkflowRuns.ToList()));
        Assert.Equal("failure", run.Status);

        var punishment = Assert.Single(Query(db => db.PunishmentEvents.ToList()));
        Assert.Equal(1L, punishment.RunId);
        Assert.Equal("alice", punishment.CulpritLogin);
        Assert.Equal(987654321L, punishment.CulpritGitHubId);
        Assert.Equal("acme/repo", punishment.RepoFullName);
        Assert.Equal("CI", punishment.WorkflowName);

        var notification = Assert.Single(Query(db => db.Notifications.ToList()));
        Assert.Equal(987654321L, notification.RecipientGitHubId);
        Assert.Equal("workflow_failed", notification.Kind);
    }

    [Fact]
    public async Task Completed_Failure_WithoutPriorRun_CreatesRunAndPunishment()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(999, "completed", conclusion: "failure"))).StatusCode);

        var run = Assert.Single(Query(db => db.WorkflowRuns.ToList()));
        Assert.Equal(999L, run.RunId);
        Assert.Equal("failure", run.Status);

        var punishment = Assert.Single(Query(db => db.PunishmentEvents.ToList()));
        Assert.Equal(999L, punishment.RunId);

        Assert.Single(Query(db => db.Notifications.ToList()));
    }

    [Fact]
    public async Task Completed_Failure_DuplicateWebhook_DoesNotDuplicateNotification()
    {
        var payload = WorkflowRunPayload(1000, "completed", conclusion: "failure");
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", payload)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", payload)).StatusCode);

        Assert.Single(Query(db => db.WorkflowRuns.ToList()));
        Assert.Single(Query(db => db.Notifications.ToList()));
        Assert.Single(Query(db => db.PunishmentEvents.ToList()));
    }

    [Fact]
    public async Task Completed_NonFailureConclusion_NoPunishment()
    {
        Assert.Equal(HttpStatusCode.OK, (await PostWebhookAsync("workflow_run", WorkflowRunPayload(1, "completed", conclusion: "cancelled"))).StatusCode);

        var run = Assert.Single(Query(db => db.WorkflowRuns.ToList()));
        Assert.Equal("cancelled", run.Status);
        Assert.Empty(Query(db => db.PunishmentEvents.ToList()));
    }

    // ───────────── dispatch / signature ─────────────

    [Fact]
    public async Task UnsupportedEvent_ReturnsOkIgnored()
    {
        var response = await PostWebhookAsync("ping", """{ "zen": "hello" }""");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Ignored", body);
    }

    [Fact]
    public async Task InvalidSignature_RejectedWhenSecretConfigured()
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("WebhookSecret", "test-secret"));
        var client = factory.CreateClient();
        var content = new StringContent(WorkflowRunPayload(1, "in_progress"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add("X-GitHub-Event", "workflow_run");
        content.Headers.Add("X-Hub-Signature-256", "sha256=wrong");

        var response = await client.PostAsync("/api/webhook/github", content);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
