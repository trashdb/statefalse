namespace Statefalse.Application;

/// <summary>
/// Persistence transaction boundary backed by the scoped DbContext. All
/// repositories in a request share the same unit of work.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<bool> TryClaimWebhookDeliveryAsync(string deliveryId, string eventType, CancellationToken cancellationToken = default);
    Task CompleteWebhookDeliveryAsync(string deliveryId, CancellationToken cancellationToken = default);
    Task ReleaseWebhookDeliveryAsync(string deliveryId, CancellationToken cancellationToken = default);
}
