using System.Web;
using Statefalse.Domain.Contracts;
using Statefalse.Application;
using Statefalse.Domain.Models;

namespace Statefalse.Application;

public sealed record AuthCallbackResponse(ApiResult? Error, string? RedirectUrl, object? OkBody);

/// <summary>
/// GitHub OAuth login flow + session endpoints (me, PAT, token resolution).
/// </summary>
public class AuthService
{
    private readonly GitHubOAuthService _oauth;
    private readonly IGitHubUserRepository _users;
    private readonly IUnitOfWork _uow;
    private readonly IConfiguration _configuration;
    private readonly JwtTokenService _jwt;
    private readonly OAuthStateStore _stateStore;

    public AuthService(GitHubOAuthService oauth, IGitHubUserRepository users, IUnitOfWork uow, IConfiguration configuration, JwtTokenService jwt, OAuthStateStore stateStore)
    {
        _oauth = oauth;
        _users = users;
        _uow = uow;
        _configuration = configuration;
        _jwt = jwt;
        _stateStore = stateStore;
    }

    public string? LoginUrl(string? redirectUri)
    {
        if (redirectUri is not null && !IsAllowedLocalRedirect(redirectUri))
            return null;

        var state = _stateStore.Create(redirectUri);
        return _oauth.GetAuthorizationUrl(state);
    }

    public async Task<AuthCallbackResponse> HandleCallbackAsync(string code, string? state)
    {
        if (string.IsNullOrEmpty(code))
            return new AuthCallbackResponse(ApiResult.BadRequest("No authorization code provided."), null, null);

        if (string.IsNullOrEmpty(state) || !_stateStore.TryConsume(state, out var redirectUri))
            return new AuthCallbackResponse(ApiResult.BadRequest("Invalid or expired OAuth state."), null, null);

        var userInfo = await _oauth.ExchangeCodeForUserInfoAsync(code);
        if (userInfo == null)
            return new AuthCallbackResponse(ApiResult.BadRequest("Failed to authenticate with GitHub."), null, null);

        // Upsert by immutable GitHubId, update username in case it changed
        var existing = await _users.FindByIdAsync(userInfo.Id);

        if (existing == null)
        {
            await _users.AddAsync(new GitHubUser
            {
                GitHubId = userInfo.Id,
                GitHubUsername = userInfo.Login,
                AccessToken = userInfo.AccessToken,
                AvatarUrl = userInfo.AvatarUrl,
                CreatedAt = DateTime.UtcNow,
                LastLoginAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.GitHubUsername = userInfo.Login;
            existing.AccessToken = userInfo.AccessToken;
            existing.AvatarUrl = userInfo.AvatarUrl;
            existing.LastLoginAt = DateTime.UtcNow;
        }

        await _uow.SaveChangesAsync();

        var token = _jwt.GenerateToken(userInfo.Id, userInfo.Login, userInfo.AvatarUrl);

        if (!string.IsNullOrEmpty(redirectUri))
        {
            var avatar = userInfo.AvatarUrl is not null ? $"&avatar={HttpUtility.UrlEncode(userInfo.AvatarUrl)}" : "";
            var callbackUri = $"{redirectUri}?id={userInfo.Id}&username={HttpUtility.UrlEncode(userInfo.Login)}{avatar}&token={HttpUtility.UrlEncode(token)}";
            return new AuthCallbackResponse(null, callbackUri, null);
        }

        return new AuthCallbackResponse(null, null, new { id = userInfo.Id, username = userInfo.Login, avatarUrl = userInfo.AvatarUrl, token });
    }

    public async Task<ApiResult> GetUsersAsync()
    {
        var users = await _users.GetAllOrderedByUsernameAsync();
        return ApiResult.Ok(users.Select(u => new UserDto(u.GitHubId, u.GitHubUsername, u.AvatarUrl)).ToList());
    }

    public async Task<ApiResult> GetMeAsync(long gitHubId)
    {
        var user = await _users.FindByIdAsync(gitHubId);

        if (user == null) return ApiResult.NotFound();

        return ApiResult.Ok(new UserProfileDto(user.GitHubId, user.GitHubUsername, user.AvatarUrl, user.UserPatToken != null));
    }

    public async Task<ApiResult> SavePatAsync(long gitHubId, string? patToken)
    {
        var user = await _users.FindByIdAsync(gitHubId);
        if (user == null) return ApiResult.NotFound();

        user.UserPatToken = string.IsNullOrWhiteSpace(patToken) ? null : patToken;
        await _uow.SaveChangesAsync();
        return ApiResult.Ok(new { saved = true });
    }

    public async Task<ApiResult> GetTokenAsync(long gitHubId)
    {
        var user = await _users.FindByIdAsync(gitHubId);
        // Mirror the fallback chain used by create-pr / merge so the client can obtain
        // the same token that already works server-side (incl. the shared global PAT).
        var token = user?.UserPatToken ?? user?.AccessToken ?? _configuration["GitHub:PatToken"];
        if (string.IsNullOrEmpty(token))
            return ApiResult.Unauthorized(new { error = "No access token found" });
        return ApiResult.Ok(new TokenDto(token));
    }

    private static bool IsAllowedLocalRedirect(string redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp
            || (uri.Host != "localhost" && uri.Host != "127.0.0.1")
            || uri.Port is < 1 or > 65535
            || uri.AbsolutePath != "/callback"
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
            return false;

        return true;
    }
}
