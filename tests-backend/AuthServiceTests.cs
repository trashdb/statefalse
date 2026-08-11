using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Statefalse.Infrastructure.Data;
using Statefalse.Domain.Models;
using Statefalse.Application;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Statefalse.Api.Tests;

[Collection("BackendIntegration")]
public class AuthServiceTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly SqliteConnection _sqliteConnection;
    private static int _counter;

    public AuthServiceTests(WebApplicationFactory<Program> factory)
    {
        _sqliteConnection = new SqliteConnection("DataSource=:memory:");
        _sqliteConnection.Open();

        var handler = new FakeGitHubOAuthHandler();

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", TestAuth.Secret);
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<AppDbContext>>();
                services.RemoveAll<AppDbContext>();
                services.AddDbContext<AppDbContext>(options =>
                    options.UseSqlite(_sqliteConnection));
                services.RemoveAll<GitHubOAuthService>();
                services.AddHttpClient<GitHubOAuthService>()
                    .ConfigurePrimaryHttpMessageHandler(() => handler);
            });
        });

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _factory.Dispose();
        _sqliteConnection.Close();
        _sqliteConnection.Dispose();
    }

    private HttpClient AuthClient(long gitHubId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer",
            TestAuth.Token(_factory, gitHubId, $"user{gitHubId}"));
        return client;
    }

    private long SeedUser(Action<GitHubUser>? configure = null)
    {
        var id = Interlocked.Increment(ref _counter) + 9000L;
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new GitHubUser
        {
            GitHubId = id,
            GitHubUsername = $"user{id}",
            CreatedAt = DateTime.UtcNow,
            LastLoginAt = DateTime.UtcNow
        };
        configure?.Invoke(user);
        db.GitHubUsers.Add(user);
        db.SaveChanges();
        return id;
    }

    // ───────────── OAuth callback ─────────────

    [Fact]
    public async Task Callback_NoCode_BadRequest()
    {
        var response = await _factory.CreateClient().GetAsync("/api/auth/callback");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Callback_NewUser_CreatesUserAndReturnsToken()
    {
        var response = await _factory.CreateClient().GetAsync("/api/auth/callback?code=abc123");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = (await response.Content.ReadFromJsonAsync<JsonElement>());
        Assert.Equal(777L, body.GetProperty("id").GetInt64());
        Assert.Equal("oauthuser", body.GetProperty("username").GetString());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = Assert.Single(db.GitHubUsers);
        Assert.Equal("oauthuser", user.GitHubUsername);
        Assert.Equal("gho_access_token", user.AccessToken);
        Assert.Equal("https://avatars.example/777.png", user.AvatarUrl);
    }

    [Fact]
    public async Task Callback_ExistingUser_UpdatesAccessToken()
    {
        var existingId = 777L;
        SeedUser(u =>
        {
            u.GitHubId = existingId;
            u.GitHubUsername = "oldname";
            u.AccessToken = "old_token";
        });

        var response = await _factory.CreateClient().GetAsync("/api/auth/callback?code=abc123");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = Assert.Single(db.GitHubUsers);
        Assert.Equal("oauthuser", user.GitHubUsername);
        Assert.Equal("gho_access_token", user.AccessToken);
    }

    [Fact]
    public async Task Callback_WithState_ReturnsRedirect()
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var state = System.Net.WebUtility.UrlEncode("statefalse://callback");
        var response = await client.GetAsync($"/api/auth/callback?code=abc123&state={state}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        Assert.StartsWith("statefalse://callback/?", response.Headers.Location!.ToString());
        Assert.Contains("id=777", response.Headers.Location!.ToString());
        Assert.Contains("token=", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task Callback_BadTokenResponse_BadRequest()
    {
        FakeGitHubOAuthHandler.NoAccessToken = true;
        try
        {
            var response = await _factory.CreateClient().GetAsync("/api/auth/callback?code=bad");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            FakeGitHubOAuthHandler.NoAccessToken = false;
        }
    }

    // ───────────── Save PAT ─────────────

    [Fact]
    public async Task SavePat_SetsToken()
    {
        var uid = SeedUser();
        var response = await AuthClient(uid).PostAsync("/api/auth/pat",
            JsonContent.Create(new { patToken = "ghp_new_pat" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal("ghp_new_pat", Assert.Single(db.GitHubUsers).UserPatToken);
    }

    [Fact]
    public async Task SavePat_Empty_ClearsToken()
    {
        var uid = SeedUser(u => u.UserPatToken = "ghp_old_pat");
        var response = await AuthClient(uid).PostAsync("/api/auth/pat",
            JsonContent.Create(new { patToken = "" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(Assert.Single(db.GitHubUsers).UserPatToken);
    }

    [Fact]
    public async Task SavePat_NoAuth_Unauthorized()
    {
        var response = await _factory.CreateClient().PostAsync("/api/auth/pat",
            JsonContent.Create(new { patToken = "ghp_x" }));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class FakeGitHubOAuthHandler : HttpMessageHandler
    {
        public static bool NoAccessToken;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://github.com/login/oauth/access_token")
            {
                var body = NoAccessToken ? "error=bad_verification_code" : "access_token=gho_access_token&scope=repo";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
            }

            var json = """
            {
              "id": 777,
              "login": "oauthuser",
              "avatar_url": "https://avatars.example/777.png"
            }
            """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
            });
        }
    }
}
