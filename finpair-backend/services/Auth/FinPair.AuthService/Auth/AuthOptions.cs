using Microsoft.AspNetCore.Http;

namespace FinPair.AuthService.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; init; } = "FinPair.AuthService";

    public string Audience { get; init; } = "FinPair.Api";

    public string SigningKey { get; init; } = "finpair-local-development-signing-key-please-change";

    public int AccessTokenLifetimeMinutes { get; init; } = 15;

    public int RefreshTokenLifetimeDays { get; init; } = 30;

    public string RefreshCookieName { get; init; } = "finpair.refresh_token";

    public string RefreshCookiePath { get; init; } = "/api/v1/auth";

    public string RefreshCookieSameSite { get; init; } = "Lax";

    public bool RefreshCookieSecure { get; init; }

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenLifetimeMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenLifetimeDays);

    public SameSiteMode RefreshCookieSameSiteMode =>
        Enum.TryParse<SameSiteMode>(RefreshCookieSameSite, ignoreCase: true, out var value)
            ? value
            : SameSiteMode.Lax;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Issuer))
        {
            throw new InvalidOperationException("Auth:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(Audience))
        {
            throw new InvalidOperationException("Auth:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(SigningKey) || SigningKey.Length < 32)
        {
            throw new InvalidOperationException("Auth:SigningKey must contain at least 32 characters.");
        }

        if (AccessTokenLifetimeMinutes <= 0)
        {
            throw new InvalidOperationException("Auth:AccessTokenLifetimeMinutes must be positive.");
        }

        if (RefreshTokenLifetimeDays <= 0)
        {
            throw new InvalidOperationException("Auth:RefreshTokenLifetimeDays must be positive.");
        }
    }
}
