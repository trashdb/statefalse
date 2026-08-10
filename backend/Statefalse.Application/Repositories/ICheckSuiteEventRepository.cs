using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// CheckSuiteEvent persistence (check_suite webhooks + live PR ciStatus lookup).
/// </summary>
public interface ICheckSuiteEventRepository
{
    Task<List<CheckSuiteEvent>> GetByShasForReposAsync(ICollection<string> headShas, ICollection<string> repos, CancellationToken cancellationToken = default);
    Task AddAsync(CheckSuiteEvent checkSuiteEvent, CancellationToken cancellationToken = default);
}
