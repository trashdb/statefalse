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
    private readonly JwtTokenService _jwt;
    private readonly OAuthStateStore _stateStore;
    private readonly OAuthCodeStore _codeStore;

    public AuthService(GitHubOAuthService oauth, IGitHubUserRepository users, IUnitOfWork uow, JwtTokenService jwt, OAuthStateStore stateStore, OAuthCodeStore codeStore)
    {
        _oauth = oauth;
        _users = users;
        _uow = uow;
        _jwt = jwt;
        _stateStore = stateStore;
        _codeStore = codeStore;
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
            var exchangeCode = _codeStore.Create(userInfo.Id, userInfo.Login, userInfo.AvatarUrl, token);
            var callbackUri = $"{redirectUri}?code={Uri.EscapeDataString(exchangeCode)}";
            return new AuthCallbackResponse(null, callbackUri, null);
        }

        return new AuthCallbackResponse(null, null, new { id = userInfo.Id, username = userInfo.Login, avatarUrl = userInfo.AvatarUrl, token });
    }

    public ApiResult ExchangeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code) || !_codeStore.TryConsume(code, out var result) || result is null)
            return ApiResult.Unauthorized(new { error = "Invalid or expired OAuth exchange code" });

        return ApiResult.Ok(result);
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
        // Never expose the server-wide PAT to a client. Backend services may still
        // use their configured fallback through IGitHubTokenResolver internally.
        var token = user?.UserPatToken ?? user?.AccessToken;
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
