using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// PunishmentEvent persistence (failed-workflow leaderboards + webhook recording).
/// </summary>
public interface IPunishmentEventRepository
{
    Task<List<PunishmentEvent>> GetRecentAsync(DateTime since, int? limit = null, CancellationToken cancellationToken = default);
    Task AddAsync(PunishmentEvent punishmentEvent, CancellationToken cancellationToken = default);
}
