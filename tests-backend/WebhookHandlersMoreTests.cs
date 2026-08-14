using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Statefalse.Infrastructure.Data;
using Statefalse.Domain.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Statefalse.Api.Tests;

[Collection("BackendIntegration")]
public class WebhookHandlersMoreTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string WebhookSecret = "test-secret";
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;

    public WebhookHandlersMoreTests(WebApplicationFactory<Program> factory)
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

    private void SeedPr(int prNumber = 42, bool draft = false, bool reviewApproved = false,
        string? approvedBy = null, string status = "open", string? headSha = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.PullRequestEvents.Add(new PullRequestEvent
        {
            PrNumber = prNumber,
            Title = $"PR #{prNumber}",
            AuthorLogin = "alice",
            AuthorGitHubId = 987654321,
            RepoFullName = "acme/repo",
            HeadBranch = "feature/x",
            BaseBranch = "main",
            PrUrl = $"https://github.com/acme/repo/pull/{prNumber}",
            Status = status,
            Draft = draft,
            ReviewApproved = reviewApproved,
            ApprovedBy = approvedBy,
            HeadSha = headSha,
            OccurredAt = DateTime.UtcNow
        });
        db.SaveChanges();
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

    // ───────────── check_suite ─────────────

    private static string CheckSuitePayload(string conclusion, string action = "completed", int prNumber = 42)
        => $$"""
        {
          "action": "{{action}}",
          "check_suite": {
            "id": 555,
            "conclusion": "{{conclusion}}",
            "head_branch": "feature/x",
            "head_sha": "deadbeef",
            "app": { "name": "GitHub Actions" },
            "pull_requests": [
              { "number": {{prNumber}}, "head": { "user": { "id": 987654321, "login": "alice" } } }
            ]
          },
          "repository": { "full_name": "acme/repo" }
        }
        """;

    [Fact]
    public async Task CheckSuite_CompletedSuccess_SavesEvent()
    {
        var response = await PostWebhookAsync("check_suite", CheckSuitePayload("success"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ev = Assert.Single(Query(db => db.CheckSuiteEvents.ToList()));
        Assert.Equal(555L, ev.CheckSuiteId);
        Assert.Equal("success", ev.Conclusion);
        Assert.Equal(42, ev.PrNumber);
        Assert.Equal("acme/repo", ev.RepoFullName);
        Assert.False(ev.WasNotified);
    }

    [Fact]
    public async Task CheckSuite_CompletedFailure_SavesEvent()
    {
        await PostWebhookAsync("check_suite", CheckSuitePayload("failure"));

        var ev = Assert.Single(Query(db => db.CheckSuiteEvents.ToList()));
        Assert.Equal("failure", ev.Conclusion);
    }

    [Fact]
    public async Task CheckSuite_CompletedNeutral_Ignored()
    {
        var response = await PostWebhookAsync("check_suite", CheckSuitePayload("neutral"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Query(db => db.CheckSuiteEvents.ToList()));
    }

    [Fact]
    public async Task CheckSuite_Requested_NoDbWrite()
    {
        var response = await PostWebhookAsync("check_suite", CheckSuitePayload("success", action: "requested"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Query(db => db.CheckSuiteEvents.ToList()));
    }

    // ───────────── pull_request ─────────────

    private static string PullRequestPayload(string action, bool merged = false, string? headSha = "abc123")
        => $$"""
        {
          "action": "{{action}}",
          "pull_request": {
            "number": 42,
            "title": "My PR",
            "html_url": "https://github.com/acme/repo/pull/42",
            "user": { "id": 987654321, "login": "alice" },
            "head": { "ref": "feature/x", "sha": "{{headSha}}" },
            "base": { "ref": "main" },
            "draft": false,
            "merged": {{merged.ToString().ToLower()}}
          },
          "repository": { "full_name": "acme/repo" }
        }
        """;

    [Fact]
    public async Task PullRequest_Opened_CreatesTrackingRow()
    {
        var response = await PostWebhookAsync("pull_request", PullRequestPayload("opened"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.Equal("open", pr.Status);
        Assert.Equal("My PR", pr.Title);
        Assert.Equal("acme/repo", pr.RepoFullName);
        Assert.Equal("feature/x", pr.HeadBranch);
        Assert.Equal("abc123", pr.HeadSha);
        Assert.Equal(987654321L, pr.AuthorGitHubId);
    }

    [Fact]
    public async Task PullRequest_Opened_DeliveredTwice_SingleRow()
    {
        await PostWebhookAsync("pull_request", PullRequestPayload("opened"));
        await PostWebhookAsync("pull_request", PullRequestPayload("opened"));

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.Equal("open", pr.Status);
    }

    [Fact]
    public async Task PullRequest_Opened_AfterSynchronize_UpdatesExistingRow()
    {
        SeedPr(headSha: "oldsha");
        await PostWebhookAsync("pull_request", PullRequestPayload("opened", headSha: "newsha"));

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.Equal("newsha", pr.HeadSha);
        Assert.Equal("open", pr.Status);
    }

    [Fact]
    public async Task PullRequest_Synchronize_ResetsApprovalAndUpdatesSha()
    {
        SeedPr(reviewApproved: true, approvedBy: "bob");
        await PostWebhookAsync("pull_request", PullRequestPayload("synchronize", headSha: "newsha"));

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.False(pr.ReviewApproved);
        Assert.Null(pr.ApprovedBy);
        Assert.Equal("newsha", pr.HeadSha);
    }

    [Fact]
    public async Task PullRequest_ReadyForReview_SetsDraftFalse()
    {
        SeedPr(draft: true);
        await PostWebhookAsync("pull_request", PullRequestPayload("ready_for_review"));

        Assert.False(Assert.Single(Query(db => db.PullRequestEvents.ToList())).Draft);
    }

    [Fact]
    public async Task PullRequest_ConvertedToDraft_SetsDraftTrue()
    {
        SeedPr(draft: false);
        await PostWebhookAsync("pull_request", PullRequestPayload("converted_to_draft"));

        Assert.True(Assert.Single(Query(db => db.PullRequestEvents.ToList())).Draft);
    }

    [Fact]
    public async Task PullRequest_ClosedMerged_SetsMergedStatus()
    {
        SeedPr();
        await PostWebhookAsync("pull_request", PullRequestPayload("closed", merged: true));

        Assert.Equal("merged", Assert.Single(Query(db => db.PullRequestEvents.ToList())).Status);
    }

    [Fact]
    public async Task PullRequest_ClosedNotMerged_SetsClosedStatus()
    {
        SeedPr();
        await PostWebhookAsync("pull_request", PullRequestPayload("closed", merged: false));

        Assert.Equal("closed", Assert.Single(Query(db => db.PullRequestEvents.ToList())).Status);
    }

    // ───────────── pull_request_review ─────────────

    private static string ReviewPayload(string state, string reviewer = "bob")
        => $$"""
        {
          "action": "submitted",
          "review": { "state": "{{state}}", "user": { "login": "{{reviewer}}" } },
          "pull_request": { "number": 42 },
          "repository": { "full_name": "acme/repo" }
        }
        """;

    [Fact]
    public async Task Review_Approved_SetsApproval()
    {
        SeedPr();
        var response = await PostWebhookAsync("pull_request_review", ReviewPayload("approved"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.True(pr.ReviewApproved);
        Assert.Equal("bob", pr.ApprovedBy);
    }

    [Fact]
    public async Task Review_ChangesRequested_ClearsApproval()
    {
        SeedPr(reviewApproved: true, approvedBy: "carol");
        await PostWebhookAsync("pull_request_review", ReviewPayload("changes_requested"));

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.False(pr.ReviewApproved);
        Assert.Null(pr.ApprovedBy);
    }

    [Fact]
    public async Task Review_Commented_DoesNotResetApproval()
    {
        SeedPr(reviewApproved: true, approvedBy: "carol");
        await PostWebhookAsync("pull_request_review", ReviewPayload("commented"));

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.True(pr.ReviewApproved);
        Assert.Equal("carol", pr.ApprovedBy);
    }

    [Fact]
    public async Task Review_UntrackedPr_Ignored()
    {
        var response = await PostWebhookAsync("pull_request_review", ReviewPayload("approved"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(Query(db => db.PullRequestEvents.ToList()));
    }

    // ───────────── issue_comment ─────────────

    private static string IssueCommentPayload(string body, string type = "User", string action = "created")
        => $$"""
        {
          "action": "{{action}}",
          "issue": { "pull_request": {} },
          "comment": { "body": "{{body}}", "html_url": "https://github.com/acme/repo/pull/42#issuecomment-1", "user": { "login": "bob", "type": "{{type}}" } },
          "pull_request": { "number": 42 },
          "repository": { "full_name": "acme/repo" }
        }
        """;

    [Fact]
    public async Task IssueComment_Created_UpdatesLastComment()
    {
        SeedPr();
        var response = await PostWebhookAsync("issue_comment", IssueCommentPayload("Looks good"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.Equal("bob", pr.LastCommentBy);
        Assert.Equal("Looks good", pr.LastCommentBody);
        Assert.NotNull(pr.LastCommentAt);
        Assert.Contains("issuecomment", pr.LastCommentUrl!);
    }

    [Fact]
    public async Task IssueComment_LongBody_TruncatedTo500()
    {
        SeedPr();
        await PostWebhookAsync("issue_comment", IssueCommentPayload(new string('x', 600)));

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.Equal(500, pr.LastCommentBody!.Length);
    }

    [Fact]
    public async Task IssueComment_Bot_Ignored()
    {
        SeedPr();
        var response = await PostWebhookAsync("issue_comment", IssueCommentPayload("spam", type: "Bot"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.Null(pr.LastCommentBy);
    }

    // ───────────── pull_request_review_comment ─────────────

    [Fact]
    public async Task ReviewComment_Created_UpdatesLastCommentWithFile()
    {
        SeedPr();
        var payload = """
        {
          "action": "created",
          "comment": { "body": "nit", "html_url": "https://github.com/acme/repo/pull/42#discussion_r1", "user": { "login": "bob", "type": "User" }, "path": "file.cs", "line": 10 },
          "pull_request": { "number": 42 },
          "repository": { "full_name": "acme/repo" }
        }
        """;
        var response = await PostWebhookAsync("pull_request_review_comment", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var pr = Assert.Single(Query(db => db.PullRequestEvents.ToList()));
        Assert.Equal("bob", pr.LastCommentBy);
        Assert.Equal("nit", pr.LastCommentBody);
    }
}
