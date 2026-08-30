using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Statefalse.Application;

public class GitHubOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string Scope { get; set; } = "read:user,repo";
}

public class GitHubOAuthService
{
    private readonly HttpClient _httpClient;
    private readonly GitHubOAuthOptions _options;

    public GitHubOAuthService(HttpClient httpClient, IOptions<GitHubOAuthOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public string GetAuthorizationUrl(string state)
    {
        var query = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["redirect_uri"] = _options.RedirectUri,
            ["scope"] = _options.Scope,
            ["state"] = state
        };
        var encodedQuery = string.Join("&", query.Select(pair =>
            $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        return "https://github.com/login/oauth/authorize?" + encodedQuery;
    }

    public async Task<GitHubUserInfo?> ExchangeCodeForUserInfoAsync(string code, CancellationToken cancellationToken = default)
    {
        var tokenRequest = new Dictionary<string, string>
        {
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
            ["code"] = code,
            ["redirect_uri"] = _options.RedirectUri
        };

        HttpResponseMessage tokenResponse;
        try
        {
            tokenResponse = await _httpClient.PostAsync(
                "https://github.com/login/oauth/access_token",
                new FormUrlEncodedContent(tokenRequest),
                cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }

        using (tokenResponse)
        {
            if (!tokenResponse.IsSuccessStatusCode)
                return null;

            var tokenContent = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);
            var queryParams = System.Web.HttpUtility.ParseQueryString(tokenContent);
            var accessToken = queryParams["access_token"];

            if (string.IsNullOrEmpty(accessToken))
                return null;

            using var userRequest = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
            userRequest.Headers.UserAgent.ParseAdd("Statefalse");
            userRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

            HttpResponseMessage userResponse;
            try
            {
                userResponse = await _httpClient.SendAsync(userRequest, cancellationToken);
            }
            catch (HttpRequestException)
            {
                return null;
            }
            catch (TaskCanceledException)
            {
                return null;
            }

            using (userResponse)
            {
                if (!userResponse.IsSuccessStatusCode)
                    return null;

                var userContent = await userResponse.Content.ReadAsStringAsync(cancellationToken);
                JsonElement userData;
                try
                {
                    userData = JsonSerializer.Deserialize<JsonElement>(userContent);
                }
                catch (JsonException)
                {
                    return null;
                }

                if (userData.ValueKind != JsonValueKind.Object
                    || !userData.TryGetProperty("id", out var idElement)
                    || !idElement.TryGetInt64(out var id)
                    || !userData.TryGetProperty("login", out var loginElement)
                    || loginElement.ValueKind != JsonValueKind.String)
                    return null;

                var login = loginElement.GetString();
                if (string.IsNullOrWhiteSpace(login))
                    return null;

                var avatarUrl = userData.TryGetProperty("avatar_url", out var av)
                    && av.ValueKind == JsonValueKind.String
                    ? av.GetString()
                    : null;

                return new GitHubUserInfo(
                    Id: id,
                    Login: login,
                    AccessToken: accessToken,
                    AvatarUrl: avatarUrl
                );
            }
        }
    }
}

public record GitHubUserInfo(long Id, string Login, string? AccessToken = null, string? AvatarUrl = null);
