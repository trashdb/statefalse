using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// PullRequestEvent persistence + queries shared by webhook handlers and
/// pull request services. Replaces the ad-hoc PullRequestQueries helper.
/// </summary>
public interface IPullRequestEventRepository
{
    Task<PullRequestEvent?> FindLatestAsync(long prNumber, string repo, CancellationToken cancellationToken = default);
    Task<PullRequestEvent?> FindLatestForUserAsync(long prNumber, string repo, long gitHubId, CancellationToken cancellationToken = default);
    Task<PullRequestEvent?> FindLatestOpenAsync(long prNumber, string repo, CancellationToken cancellationToken = default);
    Task<PullRequestEvent?> FindOpenForUserAsync(long prNumber, string repo, long gitHubId, CancellationToken cancellationToken = default);
    Task<PullRequestEvent?> FindOpenAsync(long prNumber, string repo, CancellationToken cancellationToken = default);
    Task<PullRequestEvent?> FindByRepoAndPrNumberAsync(string repo, long prNumber, CancellationToken cancellationToken = default);
    Task<List<PullRequestEvent>> GetActiveForUserAsync(long gitHubId, int page, int pageSize, DateTime mergedSince, CancellationToken cancellationToken = default);
    Task<List<PullRequestEvent>> GetMergedAsync(long prNumber, string repo, CancellationToken cancellationToken = default);
    Task<List<PullRequestEvent>> GetSubscribedToByUserAsync(long gitHubId, CancellationToken cancellationToken = default);
    Task<List<PullRequestEvent>> GetOpenForReposAndBranchesAsync(ICollection<string> repos, ICollection<string> branches, CancellationToken cancellationToken = default);
    Task<bool> AnyOpenForRepoAndBranchByUserAsync(string repo, string? branch, long gitHubId, CancellationToken cancellationToken = default);
    Task<List<string>> GetSubscribedReposAsync(long gitHubId, CancellationToken cancellationToken = default);
    Task AddAsync(PullRequestEvent pullRequestEvent, CancellationToken cancellationToken = default);
}
