using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Statefalse.Application;
using Statefalse.Infrastructure.Data;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Statefalse.Api.Tests;

/// <summary>
/// WebhookService entry point: HMAC verification, dispatch, rejection paths.
/// </summary>
[Collection("BackendIntegration")]
public class WebhookServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string Secret = "test-secret";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;

    public WebhookServiceTests(WebApplicationFactory<Program> factory)
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestAuth.Secret);
            builder.UseSetting("WebhookSecret", Secret);
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

    private static string Sign(string payload)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), Encoding.UTF8.GetBytes(payload));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(
        string payload,
        string eventType = "workflow_run",
        string? signature = null)
    {
        var client = _factory.CreateClient();
        var content = new StringContent(payload);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add("X-GitHub-Event", eventType);
        if (signature is not null)
            content.Headers.Add("X-Hub-Signature-256", signature);
        return await client.PostAsync("/api/webhook/github", content);
    }

    private static string WorkflowRunPayload(long runId, string action, string? conclusion = null)
    {
        var conclusionJson = conclusion is null ? "" : $",\"conclusion\":\"{conclusion}\"";
        return $$"""
        {
          "action": "{{action}}",
          "workflow_run": {
            "id": {{runId}},
            "name": "CI",
            "head_branch": "feature/x",
            "head_sha": "deadbeef",
            "run_started_at": "2026-08-10T08:00:00Z",
            "html_url": "https://github.com/acme/repo/actions/runs/{{runId}}",
            "event": "push",
            "pull_requests": [ { "number": 42, "merged_by": { "id": 987654321, "login": "alice" }, "user": { "id": 111222333, "login": "alice" } } ]{{conclusionJson}}
          },
          "repository": { "full_name": "acme/repo" },
          "sender": { "id": 987654321, "login": "alice" }
        }
        """;
    }

    [Fact]
    public async Task ValidSignature_ProcessesWebhook()
    {
        var payload = WorkflowRunPayload(1, "in_progress");
        var response = await PostWebhookAsync(payload, signature: Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var run = Assert.Single(Query(db => db.WorkflowRuns.ToList()));
        Assert.Equal(1L, run.RunId);
        Assert.Equal("in_progress", run.Status);
    }

    [Fact]
    public async Task ValidSignature_DispatchesToHandler()
    {
        var payload = WorkflowRunPayload(5, "completed", conclusion: "failure");
        var response = await PostWebhookAsync(payload, eventType: "workflow_run",
            signature: Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var punishment = Assert.Single(Query(db => db.PunishmentEvents.ToList()));
        Assert.Equal(5L, punishment.RunId);
        Assert.Equal("alice", punishment.CulpritLogin);
    }

    [Fact]
    public async Task MissingSignature_Unauthorized()
    {
        var response = await PostWebhookAsync(WorkflowRunPayload(1, "in_progress"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(Query(db => db.WorkflowRuns.ToList()));
    }

    [Fact]
    public async Task WrongSignature_Unauthorized()
    {
        var payload = WorkflowRunPayload(1, "in_progress");
        var response = await PostWebhookAsync(payload, signature: "sha256=deadbeef");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SignatureBoundToExactBody_TamperedBodyRejected()
    {
        var signed = WorkflowRunPayload(1, "in_progress");
        var tampered = signed.Replace("feature/x", "feature/tampered");
        var response = await PostWebhookAsync(tampered, signature: Sign(signed));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(Query(db => db.WorkflowRuns.ToList()));
    }

    [Theory]
    [InlineData("set-me-in-env-vars")]
    [InlineData("set-your-github-webhook-secret-here")]
    public async Task PlaceholderSecret_SkipsVerification(string placeholder)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
            builder.UseSetting("WebhookSecret", placeholder));
        var client = factory.CreateClient();
        var content = new StringContent(WorkflowRunPayload(1, "in_progress"));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        content.Headers.Add("X-GitHub-Event", "workflow_run");

        var response = await client.PostAsync("/api/webhook/github", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task EventTypeDispatch_IsCaseInsensitive()
    {
        var payload = WorkflowRunPayload(1, "in_progress");
        var response = await PostWebhookAsync(payload, eventType: "Workflow_Run",
            signature: Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(Query(db => db.WorkflowRuns.ToList()));
    }

    [Fact]
    public async Task UnknownEvent_WithValidSignature_IgnoredOk()
    {
        var payload = """{ "zen": "hello" }""";
        var response = await PostWebhookAsync(payload, eventType: "ping", signature: Sign(payload));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ignored", await response.Content.ReadAsStringAsync());
        Assert.Empty(Query(db => db.WorkflowRuns.ToList()));
    }

    [Fact]
    public async Task RejectedSignatures_AreLogged()
    {
        var payload = WorkflowRunPayload(1, "in_progress");
        await PostWebhookAsync(payload);
        await PostWebhookAsync(payload, signature: "sha256=wrong");

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            TestAuth.Token(_factory, 123, "alice"));
        var response = await client.GetAsync("/api/webhook/logs?limit=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        var logs = json.RootElement.EnumerateArray().ToList();
        Assert.Contains(logs, l => l.GetProperty("outcome").GetString() == "rejected"
                                   && l.GetProperty("message").GetString() == "Missing X-Hub-Signature-256");
        Assert.Contains(logs, l => l.GetProperty("outcome").GetString() == "rejected"
                                   && l.GetProperty("message").GetString() == "Invalid webhook signature");
    }
}
