using System.Security.Claims;
using System.Text.Json;
using Statefalse.Domain.Contracts;
using Statefalse.Application;

namespace Statefalse.Api;

/// <summary>
/// Minimal API endpoint definitions (replaces MVC controllers).
/// Route + rate-limit parity with the previous controllers.
/// All endpoints require a session JWT except login/callback, the signed
/// GitHub webhook, and /health.
/// </summary>
public static class ApiEndpoints
{
    public static void MapApiEndpoints(this WebApplication app)
    {
        var v1 = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .RequireRateLimiting("api");

        MapAuth(v1);
        MapPunishments(v1);
        MapPullRequests(v1);
        MapWebhook(v1);
        MapGitHub(v1);
        MapWorkflows(v1);
        MapUsers(v1);
        MapNotifications(v1);
        MapAuthCallback(app);
        MapGitHubWebhook(app);
    }

    private static long GitHubId(this HttpContext ctx)
    {
        var claim = ctx.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (claim == null || !long.TryParse(claim, out var id))
            throw new UnauthorizedAccessException("Missing identity claim.");
        return id;
    }

    private static void MapAuth(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/auth/login", (string? redirect_uri, AuthService auth) =>
        {
            var authorizationUrl = auth.LoginUrl(redirect_uri);
            return authorizationUrl is null
                ? Results.BadRequest(new { error = "Invalid local redirect URI." })
                : Results.Redirect(authorizationUrl);
        }).AllowAnonymous().RequireRateLimiting("oauth");

        routes.MapGet("/auth/me", async (HttpContext ctx, AuthService auth)
            => await MapAsync(auth.GetMeAsync(ctx.GitHubId()))).RequireAuthorization();

        routes.MapPost("/auth/exchange", async (OAuthExchangeRequest request, AuthService auth)
            => Map(auth.ExchangeCode(request.Code))).AllowAnonymous().RequireRateLimiting("oauth");

        routes.MapPost("/auth/refresh", async (RefreshTokenRequest request, AuthService auth)
            => await MapAsync(auth.RefreshAsync(request.RefreshToken))).AllowAnonymous().RequireRateLimiting("oauth");

        routes.MapPost("/auth/logout", async (RefreshTokenRequest request, AuthService auth)
            => await MapAsync(auth.LogoutAsync(request.RefreshToken))).AllowAnonymous().RequireRateLimiting("oauth");

        routes.MapPost("/auth/pat", async (HttpContext ctx, PatRequest body, AuthService auth)
            => await MapAsync(auth.SavePatAsync(ctx.GitHubId(), body.PatToken))).RequireAuthorization().RequireRateLimiting("oauth");

        routes.MapGet("/auth/token", async (HttpContext ctx, AuthService auth)
            => await MapAsync(auth.GetTokenAsync(ctx.GitHubId()))).RequireAuthorization();
    }

    private static void MapAuthCallback(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/auth/callback", async (string code, string? state, AuthService auth) =>
        {
            var result = await auth.HandleCallbackAsync(code, state);
            if (result.Error != null)
                return Results.Json(result.Error.Value, statusCode: result.Error.StatusCode);
            if (result.RedirectUrl != null)
                return Results.Redirect(result.RedirectUrl);
            return Results.Ok(result.OkBody);
        }).AllowAnonymous().RequireRateLimiting("oauth");
    }

    private static void MapPunishments(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/punishments", async (PunishmentService service, int days = 7, int limit = 50)
            => await MapAsync(service.GetRecentAsync(days, limit))).RequireAuthorization();

        routes.MapGet("/punishments/summary", async (PunishmentService service, int days = 7)
            => await MapAsync(service.GetSummaryAsync(days))).RequireAuthorization();
    }

    private static void MapNotifications(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/notifications", async (HttpContext ctx, INotificationRepository repository, int limit = 50) =>
        {
            var notifications = await repository.GetRecentForUserAsync(ctx.GitHubId(), DateTime.UtcNow.AddHours(-24), Math.Clamp(limit, 1, 100));
            return Results.Ok(notifications);
        }).RequireAuthorization().RequireRateLimiting("api");

        routes.MapPost("/notifications/{id:int}/read", async (int id, HttpContext ctx, INotificationRepository repository, IUnitOfWork uow) =>
        {
            if (!await repository.MarkAsReadAsync(ctx.GitHubId(), id))
                return Results.NotFound(new { error = "Notification not found" });
            await uow.SaveChangesAsync();
            return Results.Ok(new { read = true });
        }).RequireAuthorization().RequireRateLimiting("api");

        routes.MapPost("/notifications/read-all", async (HttpContext ctx, INotificationRepository repository) =>
            Results.Ok(new { marked = await repository.MarkAllAsReadAsync(ctx.GitHubId()) }))
            .RequireAuthorization().RequireRateLimiting("api");
    }

    private static void MapPullRequests(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/pullrequests/sync", async (HttpContext ctx, PullRequestSyncService service)
            => await MapAsync(service.SyncFromGitHubAsync(ctx.GitHubId()))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapGet("/pullrequests/active", async (PullRequestQueryService service, HttpContext ctx, int page = 1, int pageSize = 50)
            => await MapAsync(service.GetActiveAsync(ctx.GitHubId(), page, pageSize))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapGet("/pullrequests/{prNumber}/detail", async (long prNumber, string repo, HttpContext ctx, PullRequestQueryService service)
            => await MapAsync(service.GetDetailAsync(prNumber, repo, ctx.GitHubId()))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapPost("/pullrequests/{prNumber}/merge", async (PullRequestActionService service, long prNumber, string repo, HttpContext ctx, string method = "squash")
            => await MapAsync(service.MergeAsync(prNumber, repo, ctx.GitHubId(), method))).RequireAuthorization().RequireRateLimiting("action");

        routes.MapPost("/pullrequests/{prNumber}/draft", async (long prNumber, string repo, HttpContext ctx, bool draft, PullRequestActionService service)
            => await MapAsync(service.SetDraftAsync(prNumber, repo, ctx.GitHubId(), draft))).RequireAuthorization().RequireRateLimiting("action");

        routes.MapPost("/pullrequests/{prNumber}/update-branch", async (long prNumber, string repo, HttpContext ctx, PullRequestActionService service)
            => await MapAsync(service.UpdateBranchAsync(prNumber, repo, ctx.GitHubId()))).RequireAuthorization().RequireRateLimiting("action");

        routes.MapGet("/pullrequests/{prNumber}/commits", async (long prNumber, string repo, HttpContext ctx, PullRequestQueryService service)
            => await MapAsync(service.GetCommitsAsync(prNumber, repo, ctx.GitHubId()))).RequireAuthorization();

        routes.MapGet("/pullrequests/{prNumber}/files", async (long prNumber, string repo, HttpContext ctx, PullRequestQueryService service)
            => await MapAsync(service.GetFilesAsync(prNumber, repo, ctx.GitHubId()))).RequireAuthorization();

        routes.MapGet("/pullrequests/{prNumber}/checks", async (long prNumber, string repo, HttpContext ctx, PullRequestQueryService service)
            => await MapAsync(service.GetChecksAsync(prNumber, repo, ctx.GitHubId()))).RequireAuthorization();

        routes.MapPost("/pullrequests/{prNumber}/subscribe", async (long prNumber, string repo, HttpContext ctx, PullRequestSubscriptionService service)
            => await MapAsync(service.SubscribeAsync(prNumber, repo, ctx.GitHubId()))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapPost("/pullrequests/{prNumber}/unsubscribe", async (long prNumber, string repo, HttpContext ctx, PullRequestSubscriptionService service)
            => await MapAsync(service.UnsubscribeAsync(prNumber, repo, ctx.GitHubId()))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapGet("/pullrequests/{prNumber}/subscribers", async (long prNumber, string repo, PullRequestSubscriptionService service)
            => await MapAsync(service.GetSubscribersAsync(prNumber, repo))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapPost("/pullrequests/{prNumber}/add-subscriber", async (long prNumber, string repo, HttpContext ctx, string? username, long? subscriberId, PullRequestSubscriptionService service)
            => await MapAsync(service.AddSubscriberAsync(prNumber, repo, ctx.GitHubId(), username, subscriberId))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapPost("/pullrequests/{prNumber}/remove-subscriber", async (long prNumber, string repo, HttpContext ctx, long subscriberId, PullRequestSubscriptionService service)
            => await MapAsync(service.RemoveSubscriberAsync(prNumber, repo, ctx.GitHubId(), subscriberId))).RequireAuthorization().RequireRateLimiting("api");
    }

    private static void MapWebhook(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/webhook/logs", (HttpContext ctx, WebhookService service, IConfiguration configuration, int limit = 30) =>
        {
            var adminIds = configuration.GetSection("WebhookLogs:AdminGitHubIds")
                .GetChildren()
                .Select(section => long.TryParse(section.Value, out var id) ? (long?)id : null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .ToHashSet();

            if (!adminIds.Contains(ctx.GitHubId()))
                return Results.Forbid();

            return Results.Ok(service.GetLogs(Math.Clamp(limit, 1, 100)));
        }).RequireAuthorization();
    }

    private static void MapGitHubWebhook(IEndpointRouteBuilder routes)
    {
        routes.MapPost("/api/webhook/github", async (HttpContext ctx, WebhookService service) =>
        {
            var signature = ctx.Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
            var eventType = ctx.Request.Headers["X-GitHub-Event"].FirstOrDefault() ?? "";

            ctx.Request.EnableBuffering();
            var result = await service.HandleGitHubWebhookAsync(
                signatureHeader: signature,
                readRawBody: async () =>
                {
                    var body = await new StreamReader(ctx.Request.Body).ReadToEndAsync();
                    ctx.Request.Body.Position = 0;
                    return body;
                },
                readJsonBody: async () =>
                {
                    var payload = await JsonSerializer.DeserializeAsync<JsonElement>(ctx.Request.Body);
                    return payload;
                },
                eventType: eventType);

            return Results.Json(result.Value, statusCode: result.StatusCode);
        }).AllowAnonymous().RequireRateLimiting("webhook");
    }

    private static void MapGitHub(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/github/my-branches", async (HttpContext ctx, string repo, GitHubApiService service)
            => await MapAsync(service.GetMyBranchesAsync(ctx.GitHubId(), repo))).RequireAuthorization();

        routes.MapPost("/github/create-pr", async (HttpContext ctx, string repo, string head, string baseBranch,
            string title, string? body, string? subscribers, GitHubApiService service)
            => await MapAsync(service.CreatePrAsync(ctx.GitHubId(), repo, head, baseBranch, title, body, subscribers))).RequireAuthorization().RequireRateLimiting("action");

        routes.MapPost("/github/pr-preview", async (GitHubApiService service, HttpContext ctx, string repo, string head, string baseBranch,
            string title, bool useAI = true)
            => await MapAsync(service.PrPreviewAsync(ctx.GitHubId(), repo, head, baseBranch, title, useAI))).RequireAuthorization().RequireRateLimiting("action");

        routes.MapPost("/github/interpret", async (InterpretRequest request, GitHubApiService service)
            => await MapAsync(service.InterpretAsync(request))).RequireAuthorization().RequireRateLimiting("action");
    }

    private static void MapWorkflows(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/workflows/runs", async (WorkflowService service, HttpContext ctx, int limit = 20)
            => await MapAsync(service.GetRunsAsync(ctx.GitHubId(), limit))).RequireAuthorization().RequireRateLimiting("api");

        routes.MapPut("/workflows/runs/{id}/target", async (int id, SetTargetRequest request, HttpContext ctx, WorkflowService service)
            => await MapAsync(service.SetTargetAsync(id, ctx.GitHubId(), request))).RequireAuthorization();

        routes.MapPost("/workflows/runs/{runId}/rerun", async (long runId, HttpContext ctx, WorkflowService service)
            => await MapAsync(service.RerunAsync(runId, ctx.GitHubId()))).RequireAuthorization().RequireRateLimiting("action");

        routes.MapPost("/workflows/sync-active", async (HttpContext ctx, WorkflowService service)
            => await MapAsync(service.SyncActiveAsync(ctx.GitHubId()))).RequireAuthorization().RequireRateLimiting("action");
    }

    private static void MapUsers(IEndpointRouteBuilder routes)
    {
        routes.MapGet("/users", async (AuthService auth) => await MapAsync(auth.GetUsersAsync())).RequireAuthorization();
    }

    private static async Task<IResult> MapAsync(Task<ApiResult> task)
    {
        var result = await task;
        return Results.Json(result.Value, statusCode: result.StatusCode);
    }

    private static IResult Map(ApiResult result)
        => Results.Json(result.Value, statusCode: result.StatusCode);
}
