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
    private readonly IGitHubClient _github;
    private readonly IGitHubTokenResolver _tokens;

    public PullRequestSubscriptionService(
        IPullRequestEventRepository prs,
        IGitHubUserRepository users,
        IUnitOfWork uow,
        ISignalRNotifier notifier,
        INotificationRepository notifications,
        IGitHubClient github,
        IGitHubTokenResolver tokens)
    {
        _prs = prs;
        _users = users;
        _uow = uow;
        _notifier = notifier;
        _notifications = notifications;
        _github = github;
        _tokens = tokens;
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

    public async Task<ApiResult> GetSubscribersAsync(long prNumber, string repo, long gitHubId)
    {
        var pr = await _prs.FindLatestAsync(prNumber, repo);
        if (pr == null) return ApiResult.NotFound(new { error = "PR not found" });

        var ids = IdListSerializer.Deserialize(pr.SubscriberIds);
        if (pr.AuthorGitHubId != gitHubId && !ids.Contains(gitHubId))
            return ApiResult.NotFound(new { error = "PR not found" });

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

    public async Task<ApiResult> GetSubscriberCandidatesAsync(
        long prNumber,
        string repo,
        long gitHubId,
        CancellationToken cancellationToken = default)
    {
        var pr = await _prs.FindOpenAsync(prNumber, repo);
        if (pr == null) return ApiResult.NotFound(new { error = "PR not found" });
        if (pr.AuthorGitHubId != gitHubId) return ApiResult.Forbid();

        var token = await _tokens.ResolveAsync(gitHubId);
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No access token available" });

        var response = await _github.GetAsync(
            $"/repos/{repo}/collaborators?per_page=100",
            token,
            cancellationToken);
        if (response.StatusCode == 0)
            return ApiResult.FromGitHubStatus(0, new { error = "GitHub API unreachable" });
        if (response.StatusCode is < 200 or >= 300 || response.Body is not { } body || body.ValueKind != System.Text.Json.JsonValueKind.Array)
            return ApiResult.FromGitHubStatus(response.StatusCode, new { error = "Could not load repository collaborators" });

        var collaboratorIds = body.EnumerateArray()
            .Where(item => item.TryGetProperty("id", out var id) && id.TryGetInt64(out _))
            .Select(item => item.GetProperty("id").GetInt64())
            .ToHashSet();
        collaboratorIds.Remove(gitHubId);

        var existingIds = IdListSerializer.Deserialize(pr.SubscriberIds).ToHashSet();
        var users = await _users.FindConnectedByIdsAsync(collaboratorIds, cancellationToken);

        return ApiResult.Ok(users
            .Where(user => user.GitHubId != pr.AuthorGitHubId && !existingIds.Contains(user.GitHubId))
            .OrderBy(user => user.GitHubUsername)
            .Select(user => new { gitHubId = user.GitHubId, login = user.GitHubUsername, avatarUrl = user.AvatarUrl })
            .ToList());
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
