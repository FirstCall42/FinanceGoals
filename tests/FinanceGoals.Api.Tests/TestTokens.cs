using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FinanceGoals.Api.Tests;

/// <summary>Google-shaped ID tokens signed with a key the test API trusts instead of Google's.</summary>
internal static class TestTokens
{
    public const string ClientId = "test-client.apps.googleusercontent.com";

    public static readonly SymmetricSecurityKey SigningKey = new(new byte[32]) { KeyId = "test" };

    private static readonly SymmetricSecurityKey _otherKey = new(Enumerable.Repeat((byte)1, 32).ToArray()) { KeyId = "test" };

    public static string For(
        string subject = "google-subject-1",
        string email = "owner@example.com",
        string? name = "Owner",
        bool emailVerified = true,
        string audience = ClientId,
        string issuer = "https://accounts.google.com",
        DateTime? expires = null,
        bool otherKey = false)
    {
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("email", email),
            new("email_verified", emailVerified ? "true" : "false", ClaimValueTypes.Boolean),
        };
        if (name is not null)
        {
            claims.Add(new Claim("name", name));
        }

        var expiry = expires ?? DateTime.UtcNow.AddHours(1);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = expiry.AddHours(-1),
            NotBefore = expiry.AddHours(-1),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(otherKey ? _otherKey : SigningKey, SecurityAlgorithms.HmacSha256),
        });
    }
}
