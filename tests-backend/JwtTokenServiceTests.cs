using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Statefalse.Application;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Options;

namespace Statefalse.Api.Tests;

public class JwtTokenServiceTests
{
    private const string Secret = "test-secret-test-secret-test-secret-test-secret";

    private static JwtTokenService CreateService() => new(
        Options.Create(new JwtOptions
        {
            Secret = Secret,
            Issuer = "statefalse",
            Audience = "statefalse-native",
            ExpiryHours = 1
        }));

    private static ClaimsPrincipal Decode(string token)
        => new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                System.Text.Encoding.UTF8.GetBytes(Secret)),
            ValidateIssuer = true,
            ValidIssuer = "statefalse",
            ValidateAudience = true,
            ValidAudience = "statefalse-native",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out _);

    [Fact]
    public void GenerateToken_ContainsSubjectAndUsername()
    {
        var token = CreateService().GenerateToken(4242, "alice", null);
        var claims = Decode(token).Claims;

        Assert.Contains(claims, c => c.Type == ClaimTypes.NameIdentifier && c.Value == "4242");
        Assert.Contains(claims, c => c.Type == ClaimTypes.Name && c.Value == "alice");
        Assert.Contains(claims, c => c.Type == JwtRegisteredClaimNames.Jti && c.Value.Length == 32);
    }

    [Fact]
    public void GenerateToken_WithAvatar_AddsAvatarClaim()
    {
        var token = CreateService().GenerateToken(1, "bob", "https://avatars.example/bob.png");
        var claims = Decode(token).Claims;
        Assert.Contains(claims, c => c.Type == "avatar" && c.Value == "https://avatars.example/bob.png");
    }

    [Fact]
    public void GenerateToken_ExpiresInFuture()
    {
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(CreateService().GenerateToken(1, "carol", null));

        Assert.True(jwt.ValidTo > DateTime.UtcNow.AddMinutes(59));
        Assert.True(jwt.ValidTo <= DateTime.UtcNow.AddHours(2));
    }

    [Fact]
    public void JwtOptions_RejectsLongExpiry()
    {
        var options = new JwtOptions
        {
            Secret = Secret,
            ExpiryHours = 25
        };

        var exception = Assert.Throws<InvalidOperationException>(() => options.Validate());

        Assert.Contains("between 1 and 24", exception.Message);
    }
}
