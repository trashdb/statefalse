using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPullRequestEventRepository"/>.
/// </summary>
public class PullRequestEventRepository : IPullRequestEventRepository
{
    private readonly AppDbContext _db;

    public PullRequestEventRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task<PullRequestEvent?> FindLatestAsync(long prNumber, string repo, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => e.PrNumber == prNumber && e.RepoFullName == repo)
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PullRequestEvent?> FindLatestOpenAsync(long prNumber, string repo, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => e.PrNumber == prNumber && e.RepoFullName == repo && e.Status == "open")
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PullRequestEvent?> FindOpenAsync(long prNumber, string repo, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => e.PrNumber == prNumber && e.RepoFullName == repo && e.Status == "open")
            .OrderByDescending(e => e.Id)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<PullRequestEvent?> FindByRepoAndPrNumberAsync(string repo, long prNumber, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .FirstOrDefaultAsync(e => e.RepoFullName == repo && e.PrNumber == prNumber, cancellationToken);

    public Task<List<PullRequestEvent>> GetActiveForUserAsync(long gitHubId, int page, int pageSize, DateTime mergedSince, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => ((e.Status == "open" || e.Status == "in_progress") || (e.Status == "merged" && e.OccurredAt >= mergedSince))
                && (e.AuthorGitHubId == gitHubId || (e.SubscriberIds != null && e.SubscriberIds.Contains(gitHubId.ToString()))))
            .OrderByDescending(e => e.OccurredAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

    public Task<List<PullRequestEvent>> GetMergedAsync(long prNumber, string repo, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => e.PrNumber == prNumber && e.RepoFullName == repo && e.Status == "merged")
            .ToListAsync(cancellationToken);

    public Task<List<PullRequestEvent>> GetSubscribedToByUserAsync(long gitHubId, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => e.Status == "open"
                && (e.AuthorGitHubId == gitHubId
                    || (e.SubscriberIds != null && e.SubscriberIds.Contains(gitHubId.ToString()))))
            .ToListAsync(cancellationToken);

    public Task<List<PullRequestEvent>> GetOpenForReposAndBranchesAsync(ICollection<string> repos, ICollection<string> branches, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => e.Status == "open" && repos.Contains(e.RepoFullName) && e.HeadBranch != null && branches.Contains(e.HeadBranch))
            .ToListAsync(cancellationToken);

    public Task<bool> AnyOpenForRepoAndBranchByUserAsync(string repo, string? branch, long gitHubId, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents.AnyAsync(e =>
            e.Status == "open"
            && e.RepoFullName == repo
            && e.HeadBranch == branch
            && (e.AuthorGitHubId == gitHubId
                || (e.SubscriberIds != null && e.SubscriberIds.Contains(gitHubId.ToString()))), cancellationToken);

    public Task<List<string>> GetSubscribedReposAsync(long gitHubId, CancellationToken cancellationToken = default)
        => _db.PullRequestEvents
            .Where(e => (e.Status == "open"
                    || e.Status == "in_progress"
                    || (e.Status == "merged" && e.OccurredAt >= DateTime.UtcNow.AddDays(-7)))
                && (e.AuthorGitHubId == gitHubId
                    || (e.SubscriberIds != null && e.SubscriberIds.Contains(gitHubId.ToString()))))
            .Select(e => e.RepoFullName)
            .Distinct()
            .ToListAsync(cancellationToken);

    public Task AddAsync(PullRequestEvent pullRequestEvent, CancellationToken cancellationToken = default)
    {
        _db.PullRequestEvents.Add(pullRequestEvent);
        return Task.CompletedTask;
    }
}
