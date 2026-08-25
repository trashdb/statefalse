namespace Statefalse.Domain.Contracts;

public sealed record UserProfileDto(long Id, string Username, string? AvatarUrl, bool HasPat);

public sealed record OAuthExchangeRequest(string? Code);

public sealed record RefreshTokenRequest(string? RefreshToken);

public sealed record PatRequest
{
    public string? PatToken { get; set; }
}
