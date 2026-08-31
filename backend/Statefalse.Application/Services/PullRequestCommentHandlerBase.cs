using System.Text.Json;
using Statefalse.Domain.Contracts;
using Statefalse.Application;
using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// Shared logic for PR comment webhooks (issue_comment + pull_request_review_comment).
/// Both update the same LastComment* fields, then notify the author and subscribers.
/// Subclasses provide the payload accessors and ignore rules that differ per event.
/// </summary>
public abstract class PullRequestCommentHandlerBase : IWebhookHandler
{
    protected readonly IPullRequestEventRepository Prs;
    protected readonly IGitHubUserRepository Users;
    protected readonly IUnitOfWork Uow;
    protected readonly ISignalRNotifier Notifier;
    protected readonly ILogger Logger;

    private const int MaxCommentLength = 500;

    protected PullRequestCommentHandlerBase(
        IPullRequestEventRepository prs,
        IGitHubUserRepository users,
        IUnitOfWork uow,
        ISignalRNotifier notifier,
        ILogger logger)
    {
        Prs = prs;
        Users = users;
        Uow = uow;
        Notifier = notifier;
        Logger = logger;
    }

    public abstract string EventType { get; }

    public async Task<ApiResult> HandleAsync(JsonElement payload, CancellationToken cancellationToken = default)
    {
        var action = payload.GetProperty("action").GetString();
        if (action != "created")
        {
            WebhookLog.Log(EventType, action, WebhookPayload.TryGetRepo(payload), null, "ignored", $"Unsupported action '{action}'");
            return ApiResult.Ok($"Ignored: {EventType} action '{action}'.");
        }

        if (TryGetIgnoreReason(payload) is { } reason)
        {
            WebhookLog.Log(EventType, action, WebhookPayload.TryGetRepo(payload), null, "ignored", reason);
            return ApiResult.Ok($"Ignored: {reason}");
        }

        var pr = payload.GetProperty("pull_request");
        var prNumber = pr.GetProperty("number").GetInt32();
        var repo = WebhookPayload.GetRepoOrUnknown(payload);
        var commenterLogin = GetCommenterLogin(payload);
        var commentBody = GetCommentBody(payload);
        var commentUrl = GetCommentUrl(payload);

        var existing = await Prs.FindOpenAsync(prNumber, repo);

        if (existing == null)
        {
            WebhookLog.Log(EventType, action, repo, null, "ignored", "PR not tracked");
            return ApiResult.Ok("PR not tracked, ignoring.");
        }

        existing.LastCommentBy = commenterLogin;
        existing.LastCommentBody = commentBody.Length > MaxCommentLength ? commentBody[..MaxCommentLength] : commentBody;
        existing.LastCommentAt = DateTime.UtcNow;
        existing.LastCommentUrl = commentUrl;
        await Uow.SaveChangesAsync();

        WebhookLog.Log(EventType, action, repo, null, "processed",
            BuildProcessedMessage(payload, prNumber, commenterLogin));

        var notifPayload = new PrCommentedPayload(
            prNumber, repo, commenterLogin, existing.Title,
            existing.LastCommentBody, commentUrl, GetFilePath(payload), GetLine(payload));

        // Notify PR author
        if (existing.AuthorGitHubId.HasValue)
        {
            var authorConn = await Users.GetSignalRConnectionIdAsync(existing.AuthorGitHubId.Value);

            if (!string.IsNullOrEmpty(authorConn))
                await Notifier.NotifyConnectionAsync(authorConn, "PrCommented", notifPayload);
        }

        // Notify subscribers (excluding the commenter)
        var commenterUser = await Users.FindGitHubIdByUsernameAsync(commenterLogin);
        await Notifier.NotifySubscribersAsync(existing, "PrCommented", notifPayload, commenterUser);

        await Notifier.NotifyPullRequestsUpdatedAsync();
        return ApiResult.Ok(BuildResult(payload, prNumber, commenterLogin));
    }

    /// <summary>Non-null reason short-circuits the handler (e.g. not a user, not a PR comment).</summary>
    protected abstract string? TryGetIgnoreReason(JsonElement payload);
    protected abstract string GetCommenterLogin(JsonElement payload);
    protected abstract string GetCommentBody(JsonElement payload);
    protected abstract string? GetCommentUrl(JsonElement payload);
    protected virtual string? GetFilePath(JsonElement payload) => null;
    protected virtual int? GetLine(JsonElement payload) => null;
    protected virtual string BuildProcessedMessage(JsonElement payload, int prNumber, string commenterLogin)
        => $"PR #{prNumber} comment by {commenterLogin}";
    protected virtual object BuildResult(JsonElement payload, int prNumber, string commenterLogin)
        => new { prNumber, commenterLogin };
}
