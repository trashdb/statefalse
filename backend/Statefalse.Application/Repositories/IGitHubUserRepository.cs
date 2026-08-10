using Statefalse.Domain.Models;

namespace Statefalse.Application;

/// <summary>
/// GitHubUser persistence (auth, subscriptions, token resolution helpers).
/// </summary>
public interface IGitHubUserRepository
{
    Task<GitHubUser?> FindByIdAsync(long gitHubId, CancellationToken cancellationToken = default);
    Task<List<GitHubUser>> FindByIdsAsync(IReadOnlyCollection<long> gitHubIds, CancellationToken cancellationToken = default);
    Task<GitHubUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<List<GitHubUser>> GetAllOrderedByUsernameAsync(CancellationToken cancellationToken = default);
    Task<List<long>> FindGitHubIdsByUsernamesAsync(IReadOnlyCollection<string> usernames, CancellationToken cancellationToken = default);
    Task<long> FindGitHubIdByUsernameAsync(string username, CancellationToken cancellationToken = default);
    Task<string?> GetSignalRConnectionIdAsync(long gitHubId, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(long gitHubId, CancellationToken cancellationToken = default);
    Task AddAsync(GitHubUser user, CancellationToken cancellationToken = default);
}
