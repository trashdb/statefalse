using Statefalse.Domain.Contracts;
using Statefalse.Application;

namespace Statefalse.Application;

/// <summary>
/// Pull request subscriber management (who gets notified about a PR).
/// </summary>
public class PullRequestSubscriptionService
{
    private readonly IPullRequestEventRepository _prs;
    private readonly IGitHubUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly ISignalRNotifier _notifier;
    private readonly INotificationRepository _notifications;

    public PullRequestSubscriptionService(
        IPullRequestEventRepository prs,
        IGitHubUserRepository users,
        IUnitOfWork uow,
        ISignalRNotifier notifier,
        INotificationRepository notifications)
    {
        _prs = prs;
        _users = users;
        _uow = uow;
        _notifier = notifier;
        _notifications = notifications;
    }

    public async Task<ApiResult> SubscribeAsync(long prNumber, string repo, long gitHubId)
    {
        var pr = await _prs.FindOpenAsync(prNumber, repo);
        if (pr == null) return ApiResult.NotFound(new { error = "PR not found" });

        var current = IdListSerializer.Deserialize(pr.SubscriberIds);
        if (!current.Contains(gitHubId))
        {
            pr.SubscriberIds = IdListSerializer.Serialize(current.Append(gitHubId).ToArray());
            await _uow.SaveChangesAsync();
        }

        await _notifier.NotifyPullRequestsUpdatedAsync();
        return ApiResult.Ok(new { subscribed = true, subscribers = IdListSerializer.Deserialize(pr.SubscriberIds) });
    }

    public async Task<ApiResult> UnsubscribeAsync(long prNumber, string repo, long gitHubId)
    {
        var pr = await _prs.FindOpenAsync(prNumber, repo);
        if (pr == null) return ApiResult.NotFound(new { error = "PR not found" });

        var current = IdListSerializer.Deserialize(pr.SubscriberIds);
        if (current.Contains(gitHubId))
        {
            pr.SubscriberIds = IdListSerializer.Serialize(current.Where(id => id != gitHubId).ToArray());
            await _uow.SaveChangesAsync();
        }

        await _notifier.NotifyPullRequestsUpdatedAsync();
        return ApiResult.Ok(new { subscribed = false, subscribers = IdListSerializer.Deserialize(pr.SubscriberIds) });
    }

    public async Task<ApiResult> GetSubscribersAsync(long prNumber, string repo)
    {
        var pr = await _prs.FindLatestAsync(prNumber, repo);
        if (pr == null) return ApiResult.NotFound(new { error = "PR not found" });

        var ids = IdListSerializer.Deserialize(pr.SubscriberIds);

        var users = await _users.FindByIdsAsync(ids);

        return ApiResult.Ok(new { subscribers = users.Select(u => new { u.GitHubId, u.GitHubUsername, u.AvatarUrl }).ToList(), subscriberIds = ids });
    }

    public async Task<ApiResult> AddSubscriberAsync(long prNumber, string repo, long gitHubId, string? username, long? subscriberId)
    {
        var pr = await _prs.FindOpenAsync(prNumber, repo);
        if (pr == null) return ApiResult.NotFound(new { error = "PR not found" });

        // Only PR author can add subscribers (or self-subscribe)
        if (pr.AuthorGitHubId != gitHubId)
            return ApiResult.Forbid();

        long targetId;
        if (subscriberId.HasValue)
        {
            targetId = subscriberId.Value;
            var userExists = await _users.ExistsAsync(targetId);
            if (!userExists) return ApiResult.NotFound(new { error = "User not found in database" });
        }
        else if (!string.IsNullOrEmpty(username))
        {
            var user = await _users.FindByUsernameAsync(username);
            if (user == null) return ApiResult.NotFound(new { error = "User not found in database" });
            targetId = user.GitHubId;
        }
        else
        {
            return ApiResult.BadRequest(new { error = "Must provide username or subscriberId" });
        }

        var current = IdListSerializer.Deserialize(pr.SubscriberIds);
        if (!current.Contains(targetId))
        {
            pr.SubscriberIds = IdListSerializer.Serialize(current.Append(targetId).ToArray());
            var notification = new Statefalse.Domain.Models.Notification
            {
                RecipientGitHubId = targetId,
                Kind = "pr_subscribed",
                Title = "You have been added as a subscriber",
                Body = $"You have been added as a subscriber to PR #{pr.PrNumber}: {pr.Title}",
                Repo = pr.RepoFullName,
                PrNumber = pr.PrNumber,
                PrUrl = pr.PrUrl,
                CreatedAt = DateTime.UtcNow
            };
            await _notifications.AddAsync(notification);
            await _uow.SaveChangesAsync();
            await _notifier.NotifyUserAsync(targetId, "NotificationCreated", ToPayload(notification));
        }

        await _notifier.NotifyPullRequestsUpdatedAsync();
        return ApiResult.Ok(new { added = true, subscribers = IdListSerializer.Deserialize(pr.SubscriberIds) });
    }

    private static NotificationPayload ToPayload(Statefalse.Domain.Models.Notification notification)
        => new(notification.Id, notification.Kind, notification.Title, notification.Body,
            notification.Repo, notification.PrNumber, notification.PrUrl, notification.CreatedAt, notification.IsRead);

    public async Task<ApiResult> RemoveSubscriberAsync(long prNumber, string repo, long gitHubId, long subscriberId)
    {
        var pr = await _prs.FindOpenAsync(prNumber, repo);
        if (pr == null) return ApiResult.NotFound(new { error = "PR not found" });

        // Only PR author can remove subscribers (or self-unsubscribe)
        if (pr.AuthorGitHubId != gitHubId && subscriberId != gitHubId)
            return ApiResult.Forbid();

        var current = IdListSerializer.Deserialize(pr.SubscriberIds);
        if (current.Contains(subscriberId))
        {
            pr.SubscriberIds = IdListSerializer.Serialize(current.Where(id => id != subscriberId).ToArray());
            await _uow.SaveChangesAsync();
        }

        await _notifier.NotifyPullRequestsUpdatedAsync();
        return ApiResult.Ok(new { removed = true, subscribers = IdListSerializer.Deserialize(pr.SubscriberIds) });
    }
}
