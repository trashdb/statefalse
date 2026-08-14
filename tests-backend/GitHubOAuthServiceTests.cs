using System.Net;
using Statefalse.Application;
using Microsoft.Extensions.Options;

namespace Statefalse.Api.Tests;

public class GitHubOAuthServiceTests
{
    private const string ClientId = "client-123";
    private const string ClientSecret = "secret-456";

    private static GitHubOAuthService CreateService(bool noAccessToken = false)
        => new(
            new HttpClient(new FakeOAuthHandler(noAccessToken)),
            Options.Create(new GitHubOAuthOptions
            {
                ClientId = ClientId,
                ClientSecret = ClientSecret,
                RedirectUri = "statefalse://callback"
            }));

    [Fact]
    public void GetAuthorizationUrl_IncludesClientIdAndScope()
    {
        var service = CreateService();
        var url = service.GetAuthorizationUrl("opaque-state");
        var query = System.Web.HttpUtility.ParseQueryString(new Uri(url).Query);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("read:user,repo", query["scope"]);
        Assert.Equal("statefalse://callback", query["redirect_uri"]);
    }

    [Fact]
    public void GetAuthorizationUrl_EncodesOpaqueState()
    {
        var service = CreateService();
        var url = service.GetAuthorizationUrl("opaque-state");

        var state = url[(url.IndexOf("state=", StringComparison.Ordinal) + "state=".Length)..];
        Assert.Equal("opaque-state", System.Web.HttpUtility.UrlDecode(state));
        Assert.DoesNotContain("statefalse://", System.Web.HttpUtility.UrlDecode(state));
    }

    [Fact]
    public void GetAuthorizationUrl_AlwaysIncludesState()
    {
        var service = CreateService();
        var url = service.GetAuthorizationUrl("another-state");
        Assert.Contains("state=another-state", url);
    }

    [Fact]
    public async Task ExchangeCode_Success_ReturnsUserInfo()
    {
        var service = CreateService();
        var info = await service.ExchangeCodeForUserInfoAsync("code1");

        Assert.NotNull(info);
        Assert.Equal(777L, info!.Id);
        Assert.Equal("oauthuser", info.Login);
        Assert.Equal("gho_access_token", info.AccessToken);
        Assert.Equal("https://avatars.example/777.png", info.AvatarUrl);
    }

    [Fact]
    public async Task ExchangeCode_NoAccessToken_ReturnsNull()
    {
        var service = CreateService(noAccessToken: true);
        var info = await service.ExchangeCodeForUserInfoAsync("badcode");
        Assert.Null(info);
    }

    private sealed class FakeOAuthHandler : HttpMessageHandler
    {
        private readonly bool _noAccessToken;

        public FakeOAuthHandler(bool noAccessToken) => _noAccessToken = noAccessToken;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri == "https://github.com/login/oauth/access_token")
            {
                var body = _noAccessToken ? "error=bad_verification_code" : "access_token=gho_access_token&scope=repo";
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
