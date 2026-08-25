using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Statefalse.Application;

public sealed class OAuthStateStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Create(string? redirectUri)
    {
        var state = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        _entries[state] = new Entry(redirectUri, DateTimeOffset.UtcNow.Add(Lifetime));
        RemoveExpired();
        return state;
    }

    public bool TryConsume(string state, out string? redirectUri)
    {
        redirectUri = null;
        if (!_entries.TryRemove(state, out var entry))
            return false;
        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            return false;
        redirectUri = entry.RedirectUri;
        return true;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
                _entries.TryRemove(pair.Key, out _);
        }
    }

    private sealed record Entry(string? RedirectUri, DateTimeOffset ExpiresAt);
}

public sealed class OAuthCodeStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public string Create(long id, string username, string? avatarUrl, string token, string refreshToken, int expiresIn)
    {
        var code = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        _entries[code] = new Entry(id, username, avatarUrl, token, refreshToken, expiresIn, DateTimeOffset.UtcNow.Add(Lifetime));
        RemoveExpired();
        return code;
    }

    public string Create(long id, string username, string? avatarUrl, string token)
        => Create(id, username, avatarUrl, token, string.Empty, 0);

    public bool TryConsume(string code, out OAuthExchangeResult? result)
    {
        result = null;
        if (!_entries.TryRemove(code, out var entry) || entry.ExpiresAt <= DateTimeOffset.UtcNow)
            return false;

        result = new OAuthExchangeResult(entry.Id, entry.Username, entry.AvatarUrl, entry.Token, entry.RefreshToken, entry.ExpiresIn);
        return true;
    }

    private void RemoveExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var pair in _entries)
        {
            if (pair.Value.ExpiresAt <= now)
                _entries.TryRemove(pair.Key, out _);
        }
    }

    private sealed record Entry(long Id, string Username, string? AvatarUrl, string Token, string RefreshToken, int ExpiresIn, DateTimeOffset ExpiresAt);
}

public sealed record OAuthExchangeResult(long Id, string Username, string? AvatarUrl, string Token, string RefreshToken = "", int ExpiresIn = 0);

