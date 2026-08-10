namespace Statefalse.Application;

/// <summary>
/// Persistence transaction boundary backed by the scoped DbContext. All
/// repositories in a request share the same unit of work.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
