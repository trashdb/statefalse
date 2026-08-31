using Statefalse.Domain.Models;

namespace Statefalse.Application;

public enum GitHubTokenSource
{
    None,
    Pat,
    OAuth
}

/// <summary>
/// Resolves the effective GitHub token for a user. Precedence:
/// User PAT > OAuth access token > shared server PAT.
/// </summary>
public interface IGitHubTokenResolver
{
    Task<GitHubUser?> GetUserAsync(long gitHubId);

    string? ResolveForUser(GitHubUser? user);

    GitHubTokenSource ResolveSourceForUser(GitHubUser? user);

    string? ResolveOAuthForUser(GitHubUser? user);

    Task<string?> ResolveAsync(long gitHubId);


    Task<GitHubUser?> FindByLoginAsync(string login);

    Task<GitHubUser?> FindConnectedUserAsync(string login, long? gitHubId);
}
