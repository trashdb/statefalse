using Statefalse.Domain.Models;

namespace Statefalse.Application;

public interface INotificationRepository
{
    Task AddAsync(Notification notification, CancellationToken cancellationToken = default);
    Task<List<Notification>> GetRecentForUserAsync(long gitHubId, DateTime since, int limit, CancellationToken cancellationToken = default);
    Task<bool> MarkAsReadAsync(long gitHubId, int notificationId, CancellationToken cancellationToken = default);
    Task<int> MarkAllAsReadAsync(long gitHubId, CancellationToken cancellationToken = default);
}

