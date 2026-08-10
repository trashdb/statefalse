using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ICheckSuiteEventRepository"/>.
/// </summary>
public class CheckSuiteEventRepository : ICheckSuiteEventRepository
{
    private readonly AppDbContext _db;

    public CheckSuiteEventRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<List<CheckSuiteEvent>> GetByShasForReposAsync(ICollection<string> headShas, ICollection<string> repos, CancellationToken cancellationToken = default)
        => _db.CheckSuiteEvents
            .Where(c => c.HeadSha != null && repos.Contains(c.RepoFullName) && headShas.Contains(c.HeadSha))
            .ToListAsync(cancellationToken);

    public Task AddAsync(CheckSuiteEvent checkSuiteEvent, CancellationToken cancellationToken = default)
    {
        _db.CheckSuiteEvents.Add(checkSuiteEvent);
        return Task.CompletedTask;
    }
}
