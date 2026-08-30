using Microsoft.EntityFrameworkCore;
using Statefalse.Application;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure;

/// <summary>
/// Encrypts legacy plaintext credentials once, without logging their values.
/// </summary>
public sealed class GitHubCredentialMigrationService
{
    private readonly AppDbContext _db;
    private readonly IGitHubCredentialProtector _protector;

    public GitHubCredentialMigrationService(AppDbContext db, IGitHubCredentialProtector protector)
    {
        _db = db;
        _protector = protector;
    }

    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var users = await _db.GitHubUsers
            .Where(u => u.AccessToken != null || u.UserPatToken != null)
            .ToListAsync(cancellationToken);
        var migrated = 0;

        foreach (var user in users)
        {
            var changed = false;
            if (!string.IsNullOrEmpty(user.AccessToken) && _protector.NeedsReEncryption(user.AccessToken))
            {
                user.AccessToken = _protector.Protect(user.AccessToken);
                changed = true;
            }

            if (!string.IsNullOrEmpty(user.UserPatToken) && _protector.NeedsReEncryption(user.UserPatToken))
            {
                user.UserPatToken = _protector.Protect(user.UserPatToken);
                changed = true;
            }

            if (changed)
                migrated++;
        }

        if (migrated > 0)
            await _db.SaveChangesAsync(cancellationToken);

        return migrated;
    }
}
