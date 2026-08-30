using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Statefalse.Application;
using Statefalse.Domain.Models;
using Statefalse.Infrastructure.Data;

namespace Statefalse.Infrastructure.Services;

public sealed class RefreshTokenService : IRefreshTokenService
{
    private static readonly SemaphoreSlim RotationGate = new(1, 1);
    private readonly AppDbContext _db;
    private readonly JwtTokenService _jwt;
    private readonly JwtOptions _options;

    public RefreshTokenService(AppDbContext db, JwtTokenService jwt, IOptions<JwtOptions> options)
    {
        _db = db;
        _jwt = jwt;
        _options = options.Value;
    }

    public async Task<AuthTokenResult> CreateAsync(long gitHubId, string username, string? avatarUrl, CancellationToken cancellationToken = default)
    {
        var refreshToken = RefreshTokenHash.Create();
        _db.RefreshTokens.Add(new RefreshToken
        {
            GitHubId = gitHubId,
            TokenHash = RefreshTokenHash.Compute(refreshToken),
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(_options.RefreshTokenExpiryDays)
        });
        await _db.SaveChangesAsync(cancellationToken);
        return CreateResult(gitHubId, username, avatarUrl, refreshToken);
    }

    public async Task<AuthTokenResult?> RotateAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return null;

        var hash = RefreshTokenHash.Compute(refreshToken);
        var replacement = RefreshTokenHash.Create();
        var replacementHash = RefreshTokenHash.Compute(replacement);
        var now = DateTime.UtcNow;

        await RotationGate.WaitAsync(cancellationToken);
        try
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
            var revoked = await _db.RefreshTokens
                .Where(t => t.TokenHash == hash && t.RevokedAt == null && t.ExpiresAt > now)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.ReplacedByTokenHash, replacementHash), cancellationToken);

            if (revoked != 1)
                return null;

            var stored = await _db.RefreshTokens
                .AsNoTracking()
                .SingleAsync(t => t.TokenHash == hash, cancellationToken);
            var user = await _db.GitHubUsers
                .AsNoTracking()
                .SingleOrDefaultAsync(u => u.GitHubId == stored.GitHubId, cancellationToken);
            if (user == null)
                return null;

            _db.RefreshTokens.Add(new RefreshToken
            {
                GitHubId = user.GitHubId,
                TokenHash = replacementHash,
                CreatedAt = now,
                ExpiresAt = now.AddDays(_options.RefreshTokenExpiryDays)
            });
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return CreateResult(user.GitHubId, user.GitHubUsername, user.AvatarUrl, replacement);
        }
        finally
        {
            RotationGate.Release();
        }
    }

    public async Task<bool> RevokeAsync(string? refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            return false;

        var stored = await _db.RefreshTokens.SingleOrDefaultAsync(
            t => t.TokenHash == RefreshTokenHash.Compute(refreshToken), cancellationToken);
        if (stored == null || stored.RevokedAt != null)
            return false;

        stored.RevokedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public Task<int> RevokeAllAsync(long gitHubId, CancellationToken cancellationToken = default)
        => _db.RefreshTokens
            .Where(t => t.GitHubId == gitHubId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, DateTime.UtcNow), cancellationToken);

    private AuthTokenResult CreateResult(long id, string username, string? avatarUrl, string refreshToken)
        => new(id, username, avatarUrl, _jwt.GenerateToken(id, username, avatarUrl), refreshToken,
            checked(_options.ExpiryHours * 60 * 60));
}
