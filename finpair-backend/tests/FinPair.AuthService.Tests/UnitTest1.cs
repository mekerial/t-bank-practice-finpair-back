namespace FinPair.AuthService.Tests;

using FinPair.AuthService.Auth;
using System.Reflection;
using Microsoft.Extensions.Options;
using Moq;

public class AuthCryptoTests
{
    [Fact]
    public void PasswordHasher_VerifiesOriginalPassword()
    {
        var passwordHasher = new PasswordHasher();

        var hash = passwordHasher.Hash("StrongPass123!");

        Assert.True(passwordHasher.Verify("StrongPass123!", hash));
        Assert.False(passwordHasher.Verify("wrong-password", hash));
    }

    [Fact]
    public void JwtTokenService_ValidatesCreatedAccessToken()
    {
        var tokenService = CreateTokenService();
        var user = new UserRecord(
            Guid.NewGuid(),
            "user@example.com",
            "hash",
            "User",
            null,
            false);

        var token = tokenService.CreateAccessToken(user);

        Assert.True(tokenService.TryValidateAccessToken(token, out var principal));
        Assert.Equal(user.Id.ToString(), principal.FindFirst("sub")?.Value);
    }

    [Fact]
    public void JwtTokenService_RejectsTamperedAccessToken()
    {
        var tokenService = CreateTokenService();
        var user = new UserRecord(
            Guid.NewGuid(),
            "user@example.com",
            "hash",
            "User",
            null,
            false);

        var token = tokenService.CreateAccessToken(user);
        var tamperedToken = token[..^1] + (token[^1] == 'a' ? 'b' : 'a');

        Assert.False(tokenService.TryValidateAccessToken(tamperedToken, out _));
    }

    [Fact]
    public void AuthEndpoints_UserSummary_IncludesName()
    {
        var user = new UserRecord(
            Guid.NewGuid(),
            "user@example.com",
            "hash",
            "Partner A",
            null,
            false);

        var method = typeof(AuthEndpoints)
            .GetMethod("ToUserSummary", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(nameof(AuthEndpoints), "ToUserSummary");

        var summary = (UserSummary)method.Invoke(null, [user])!;

        Assert.Equal("Partner A", summary.Name);
    }

    private static JwtTokenService CreateTokenService()
    {
        var options = new Mock<IOptions<AuthOptions>>();
        options.SetupGet(item => item.Value).Returns(new AuthOptions
        {
            Issuer = "FinPair.AuthService.Tests",
            Audience = "FinPair.Api.Tests",
            SigningKey = "finpair-auth-service-tests-signing-key"
        });

        return new JwtTokenService(options.Object);
    }
}
