using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Statefalse.Application;
using System.Text.Json;

namespace Statefalse.Api.Tests;

/// <summary>
/// Shared JWT test-secret + token helper for integration tests.
/// </summary>
public static class TestAuth
{
    public const string Secret = "test-secret-key-0123456789abcdef0123456789abcdef";

    public static string Token(WebApplicationFactory<Program> factory, long gitHubId, string username)
    {
        using var scope = factory.Services.CreateScope();
        var jwt = scope.ServiceProvider.GetRequiredService<JwtTokenService>();
        return jwt.GenerateToken(gitHubId, username, null);
    }
}
internal sealed class TestGitHubClient : IGitHubClient
{
    public Task<GitHubResponse> GetAsync(string path, string? token = null, CancellationToken ct = default)
    {
        if (path == "/user" && token is not null
            && long.TryParse(token[(token.LastIndexOf('_') + 1)..], out var id))
            return Task.FromResult(new GitHubResponse(200, JsonSerializer.SerializeToElement(new { id })));

        return Task.FromResult(new GitHubResponse(404, null));
    }

    public Task<GitHubResponse> PostAsync(string path, string? token, object? body = null, CancellationToken ct = default)
        => Task.FromResult(new GitHubResponse(404, null));

    public Task<GitHubResponse> PutAsync(string path, string? token, object? body = null, CancellationToken ct = default)
        => Task.FromResult(new GitHubResponse(404, null));

    public Task<GitHubResponse> GraphQlAsync(string query, string? token, CancellationToken ct = default)
        => Task.FromResult(new GitHubResponse(404, null));
}
