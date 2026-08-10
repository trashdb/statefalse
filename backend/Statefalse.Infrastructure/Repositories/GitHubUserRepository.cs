using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IGitHubUserRepository"/>.
/// </summary>
public class GitHubUserRepository : IGitHubUserRepository
{
    private readonly AppDbContext _db;

    public GitHubUserRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<GitHubUser?> FindByIdAsync(long gitHubId, CancellationToken cancellationToken = default)
        => _db.GitHubUsers.FirstOrDefaultAsync(u => u.GitHubId == gitHubId, cancellationToken);

    public Task<List<GitHubUser>> FindByIdsAsync(IReadOnlyCollection<long> gitHubIds, CancellationToken cancellationToken = default)
        => _db.GitHubUsers
            .Where(u => gitHubIds.Contains(u.GitHubId))
            .ToListAsync(cancellationToken);

    public Task<GitHubUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => _db.GitHubUsers.FirstOrDefaultAsync(u => u.GitHubUsername == username, cancellationToken);

    public Task<List<GitHubUser>> GetAllOrderedByUsernameAsync(CancellationToken cancellationToken = default)
        => _db.GitHubUsers
            .OrderBy(u => u.GitHubUsername)
            .ToListAsync(cancellationToken);

    public Task<List<long>> FindGitHubIdsByUsernamesAsync(IReadOnlyCollection<string> usernames, CancellationToken cancellationToken = default)
        => _db.GitHubUsers
            .Where(u => usernames.Contains(u.GitHubUsername))
            .Select(u => u.GitHubId)
            .ToListAsync(cancellationToken);

    public Task<long> FindGitHubIdByUsernameAsync(string username, CancellationToken cancellationToken = default)
        => _db.GitHubUsers
            .Where(u => u.GitHubUsername == username)
            .Select(u => u.GitHubId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<string?> GetSignalRConnectionIdAsync(long gitHubId, CancellationToken cancellationToken = default)
        => _db.GitHubUsers
            .Where(u => u.GitHubId == gitHubId)
            .Select(u => u.SignalRConnectionId)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> ExistsAsync(long gitHubId, CancellationToken cancellationToken = default)
        => _db.GitHubUsers.AnyAsync(u => u.GitHubId == gitHubId, cancellationToken);

    public Task AddAsync(GitHubUser user, CancellationToken cancellationToken = default)
    {
        _db.GitHubUsers.Add(user);
        return Task.CompletedTask;
    }
}
