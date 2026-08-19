using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private readonly AppDbContext _db;

    public NotificationRepository(AppDbContext db) => _db = db;

    public Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        _db.Set<Notification>().Add(notification);
        return Task.CompletedTask;
    }

    public Task<List<Notification>> GetRecentForUserAsync(long gitHubId, DateTime since, int limit, CancellationToken cancellationToken = default)
        => _db.Set<Notification>()
            .Where(n => n.RecipientGitHubId == gitHubId && n.CreatedAt >= since)
            .OrderByDescending(n => n.CreatedAt)
            .Take(Math.Clamp(limit, 1, 100))
            .ToListAsync(cancellationToken);

    public async Task<bool> MarkAsReadAsync(long gitHubId, int notificationId, CancellationToken cancellationToken = default)
    {
        var notification = await _db.Set<Notification>()
            .FirstOrDefaultAsync(n => n.Id == notificationId && n.RecipientGitHubId == gitHubId, cancellationToken);
        if (notification == null) return false;
        notification.IsRead = true;
        return true;
    }

    public Task<int> MarkAllAsReadAsync(long gitHubId, CancellationToken cancellationToken = default)
        => _db.Set<Notification>()
            .Where(n => n.RecipientGitHubId == gitHubId && !n.IsRead)
            .ExecuteUpdateAsync(setters => setters.SetProperty(n => n.IsRead, true), cancellationToken);
}

