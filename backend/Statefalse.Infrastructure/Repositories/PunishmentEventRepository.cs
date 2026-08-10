using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPunishmentEventRepository"/>.
/// </summary>
public class PunishmentEventRepository : IPunishmentEventRepository
{
    private readonly AppDbContext _db;

    public PunishmentEventRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<List<PunishmentEvent>> GetRecentAsync(DateTime since, int? limit = null, CancellationToken cancellationToken = default)
    {
        IQueryable<PunishmentEvent> query = _db.PunishmentEvents
            .Where(e => e.OccurredAt >= since)
            .OrderByDescending(e => e.OccurredAt);
        if (limit.HasValue)
            query = query.Take(limit.Value);
        return query.ToListAsync(cancellationToken);
    }

    public Task AddAsync(PunishmentEvent punishmentEvent, CancellationToken cancellationToken = default)
    {
        _db.PunishmentEvents.Add(punishmentEvent);
        return Task.CompletedTask;
    }
}
