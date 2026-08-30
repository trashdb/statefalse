using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Infrastructure.Data;
using Statefalse.Domain.Models;

namespace Statefalse.Infrastructure;

/// <summary>
/// Resolves the effective GitHub token for a user. Precedence:
/// User PAT > OAuth access token > shared server PAT.
/// </summary>
public class GitHubTokenResolver : IGitHubTokenResolver
{
    private readonly AppDbContext _db;
    private readonly IGitHubCredentialProtector _protector;

    public GitHubTokenResolver(AppDbContext db, IGitHubCredentialProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<GitHubUser?> GetUserAsync(long gitHubId)
        => await _db.GitHubUsers.FirstOrDefaultAsync(u => u.GitHubId == gitHubId);

    public string? ResolveForUser(GitHubUser? user)
        => _protector.Unprotect(user?.UserPatToken)
            ?? _protector.Unprotect(user?.AccessToken);

    public string? ResolveOAuthForUser(GitHubUser? user)
        => _protector.Unprotect(user?.AccessToken);

    public async Task<string?> ResolveAsync(long gitHubId)
        => ResolveForUser(await GetUserAsync(gitHubId));


    public async Task<GitHubUser?> FindByLoginAsync(string login)
        => await _db.GitHubUsers.FirstOrDefaultAsync(u => u.GitHubUsername == login);

    public async Task<GitHubUser?> FindConnectedUserAsync(string login, long? gitHubId)
        => gitHubId.HasValue
            ? await _db.GitHubUsers.FirstOrDefaultAsync(u => u.GitHubId == gitHubId.Value && u.SignalRConnectionId != null)
            : await _db.GitHubUsers.FirstOrDefaultAsync(u => u.GitHubUsername == login && u.SignalRConnectionId != null);
}
