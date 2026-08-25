using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Statefalse.Application;

/// <summary>
/// Configuration for issued session JWTs.
/// </summary>
public sealed class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "statefalse";
    public string Audience { get; set; } = "statefalse-native";
    public int ExpiryHours { get; set; } = 12;
    public int RefreshTokenExpiryDays { get; set; } = 30;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Secret) || Encoding.UTF8.GetByteCount(Secret) < 32)
            throw new InvalidOperationException("Jwt:Secret must be set and at least 32 bytes long.");
        if (string.IsNullOrWhiteSpace(Issuer))
            throw new InvalidOperationException("Jwt:Issuer must be configured.");
        if (string.IsNullOrWhiteSpace(Audience))
            throw new InvalidOperationException("Jwt:Audience must be configured.");
        if (ExpiryHours is < 1 or > 24)
            throw new InvalidOperationException("Jwt:ExpiryHours must be between 1 and 24 hours.");
        if (RefreshTokenExpiryDays is < 1 or > 365)
            throw new InvalidOperationException("Jwt:RefreshTokenExpiryDays must be between 1 and 365 days.");
    }
}

/// <summary>
/// Issues short-lived session JWTs for authenticated GitHub users.
/// Stateless: no server-side session store, revocation happens at expiry.
/// </summary>
public class JwtTokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options)
    {
        _options = options.Value;
    }

    public string GenerateToken(long gitHubId, string username, string? avatarUrl)
    {
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, gitHubId.ToString()),
            new(JwtRegisteredClaimNames.UniqueName, username),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N"))
        };
        if (!string.IsNullOrEmpty(avatarUrl))
            claims.Add(new Claim("avatar", avatarUrl));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.Secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(_options.ExpiryHours),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
