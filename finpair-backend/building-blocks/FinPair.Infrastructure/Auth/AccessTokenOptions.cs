namespace FinPair.Infrastructure.Auth;

public sealed class AccessTokenOptions
{
    public const string SectionName = "Auth";

    public string Issuer { get; init; } = "FinPair.AuthService";

    public string Audience { get; init; } = "FinPair.Api";

    public string SigningKey { get; init; } = "finpair-local-development-signing-key-please-change";

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
    }
}
