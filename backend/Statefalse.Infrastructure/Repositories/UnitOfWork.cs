using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IUnitOfWork"/> over AppDbContext.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public UnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => _db.SaveChangesAsync(cancellationToken);

    public async Task<bool> TryClaimWebhookDeliveryAsync(
        string deliveryId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        if (await _db.WebhookDeliveries.AnyAsync(d => d.DeliveryId == deliveryId, cancellationToken))
            return false;

        _db.WebhookDeliveries.Add(new WebhookDelivery
        {
            DeliveryId = deliveryId,
            EventType = eventType,
            ReceivedAt = DateTime.UtcNow
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }

    public async Task CompleteWebhookDeliveryAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        var delivery = await _db.WebhookDeliveries
            .SingleAsync(d => d.DeliveryId == deliveryId, cancellationToken);
        delivery.ProcessedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task ReleaseWebhookDeliveryAsync(string deliveryId, CancellationToken cancellationToken = default)
    {
        var delivery = await _db.WebhookDeliveries
            .SingleOrDefaultAsync(d => d.DeliveryId == deliveryId, cancellationToken);
        if (delivery is null)
            return;

        _db.WebhookDeliveries.Remove(delivery);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
