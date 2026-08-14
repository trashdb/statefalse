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

