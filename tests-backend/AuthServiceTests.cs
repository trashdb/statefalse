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

    private async Task<string> BeginOAuthAsync(string? redirectUri = null)
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var path = redirectUri is null
            ? "/api/v1/auth/login"
            : $"/api/v1/auth/login?redirect_uri={Uri.EscapeDataString(redirectUri)}";
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location;
        Assert.NotNull(location);
        var state = System.Web.HttpUtility.ParseQueryString(location.Query)["state"];
        Assert.NotNull(state);
        return state!;
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
    public async Task Callback_InvalidState_BadRequest()
    {
        var response = await _factory.CreateClient().GetAsync(
            "/api/auth/callback?code=abc123&state=unknown-state");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Callback_NewUser_CreatesUserAndReturnsToken()
    {
        var state = await BeginOAuthAsync();
        var response = await _factory.CreateClient().GetAsync($"/api/auth/callback?code=abc123&state={Uri.EscapeDataString(state)}");
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

        var state = await BeginOAuthAsync();
        var response = await _factory.CreateClient().GetAsync($"/api/auth/callback?code=abc123&state={Uri.EscapeDataString(state)}");
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
        var state = await BeginOAuthAsync("http://localhost:51623/callback");
        var response = await client.GetAsync($"/api/auth/callback?code=abc123&state={Uri.EscapeDataString(state)}");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);

        var location = response.Headers.Location!;
        Assert.StartsWith("http://localhost:51623/callback?", location.ToString());
        var query = System.Web.HttpUtility.ParseQueryString(location.Query);
        Assert.False(string.IsNullOrEmpty(query["code"]));
        Assert.Null(query["token"]);
        Assert.Null(query["id"]);
    }

    [Fact]
    public async Task ExchangeCode_ReturnsSessionAndCanOnlyBeUsedOnce()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var state = await BeginOAuthAsync("http://localhost:51624/callback");
        var callback = await client.GetAsync($"/api/auth/callback?code=abc123&state={Uri.EscapeDataString(state)}");
        var location = callback.Headers.Location!;
        var code = System.Web.HttpUtility.ParseQueryString(location.Query)["code"];
        Assert.False(string.IsNullOrEmpty(code));

        var exchange = await client.PostAsJsonAsync("/api/v1/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, exchange.StatusCode);
        var body = await exchange.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(777L, body.GetProperty("id").GetInt64());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("token").GetString()));

        var replay = await client.PostAsJsonAsync("/api/v1/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
    }

    [Fact]
    public async Task ExchangeCode_InvalidCode_Unauthorized()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            "/api/v1/auth/exchange", new { code = "invalid-code" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_RotatesTokenAndLogoutRevokesReplacement()
    {
        var state = await BeginOAuthAsync();
        var client = _factory.CreateClient();
        var callback = await client.GetAsync($"/api/auth/callback?code=abc123&state={Uri.EscapeDataString(state)}");
        var initial = await callback.Content.ReadFromJsonAsync<JsonElement>();
        var originalRefresh = initial.GetProperty("refreshToken").GetString();
        Assert.False(string.IsNullOrWhiteSpace(originalRefresh));

        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = originalRefresh });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = await refresh.Content.ReadFromJsonAsync<JsonElement>();
        var replacementRefresh = rotated.GetProperty("refreshToken").GetString();
        Assert.NotEqual(originalRefresh, replacementRefresh);
        Assert.False(string.IsNullOrWhiteSpace(rotated.GetProperty("token").GetString()));

        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = originalRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);

        var logout = await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = replacementRefresh });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var afterLogout = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = replacementRefresh });
        Assert.Equal(HttpStatusCode.Unauthorized, afterLogout.StatusCode);
    }

    [Fact]
    public async Task Callback_StateCanOnlyBeConsumedOnce()
    {
        var state = await BeginOAuthAsync();
        var client = _factory.CreateClient();
        var callback = $"/api/auth/callback?code=abc123&state={Uri.EscapeDataString(state)}";

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(callback)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(callback)).StatusCode);
    }

    [Fact]
    public async Task Callback_BadTokenResponse_BadRequest()
    {
        FakeGitHubOAuthHandler.NoAccessToken = true;
        try
        {
            var state = await BeginOAuthAsync();
            var response = await _factory.CreateClient().GetAsync($"/api/auth/callback?code=bad&state={Uri.EscapeDataString(state)}");
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
        var response = await AuthClient(uid).PostAsync("/api/v1/auth/pat",
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
        var response = await AuthClient(uid).PostAsync("/api/v1/auth/pat",
            JsonContent.Create(new { patToken = "" }));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null(Assert.Single(db.GitHubUsers).UserPatToken);
    }

    [Fact]
    public async Task SavePat_NoAuth_Unauthorized()
    {
        var response = await _factory.CreateClient().PostAsync("/api/v1/auth/pat",
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
