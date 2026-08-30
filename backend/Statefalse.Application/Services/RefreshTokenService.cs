using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Statefalse.Domain.Models;

namespace Statefalse.Application;

public sealed record AuthTokenResult(
    long Id,
    string Username,
    string? AvatarUrl,
    string Token,
    string RefreshToken,
    int ExpiresIn);

public interface IRefreshTokenService
{
    Task<AuthTokenResult> CreateAsync(long gitHubId, string username, string? avatarUrl, CancellationToken cancellationToken = default);
    Task<AuthTokenResult?> RotateAsync(string? refreshToken, CancellationToken cancellationToken = default);
    Task<bool> RevokeAsync(string? refreshToken, CancellationToken cancellationToken = default);
    Task<int> RevokeAllAsync(long gitHubId, CancellationToken cancellationToken = default);
}

public static class RefreshTokenHash
{
    public static string Compute(string token)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

    public static string Create()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
}
