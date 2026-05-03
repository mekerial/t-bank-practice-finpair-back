using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FinPair.AuthService.Auth;

public sealed class JwtTokenService
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly AuthOptions _options;
    private readonly byte[] _signingKey;

    public JwtTokenService(IOptions<AuthOptions> options)
    {
        _options = options.Value;
        _options.Validate();
        _signingKey = Encoding.UTF8.GetBytes(_options.SigningKey);
    }

    public int AccessTokenExpiresInSeconds => (int)_options.AccessTokenLifetime.TotalSeconds;

    public string CreateAccessToken(UserRecord user, DateTimeOffset? issuedAt = null)
    {
        var now = issuedAt ?? DateTimeOffset.UtcNow;
        var header = new Dictionary<string, object?>
        {
            ["alg"] = "HS256",
            ["typ"] = "JWT"
        };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = _options.Issuer,
            ["aud"] = _options.Audience,
            ["sub"] = user.Id.ToString(),
            ["email"] = user.Email,
            ["email_verified"] = user.EmailVerified,
            ["has_partner"] = user.HouseholdId is not null,
            ["jti"] = Guid.NewGuid().ToString(),
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.Add(_options.AccessTokenLifetime).ToUnixTimeSeconds()
        };

        var encodedHeader = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(header));
        var encodedPayload = Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var data = $"{encodedHeader}.{encodedPayload}";

        return $"{data}.{CreateSignature(data)}";
    }

    public string CreateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Base64Url.Encode(bytes);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Base64Url.Encode(hash);
    }

    public bool TryValidateAccessToken(string token, out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal(new ClaimsIdentity());

        var parts = token.Split('.');
        if (parts.Length != 3)
        {
            return false;
        }

        var data = $"{parts[0]}.{parts[1]}";
        var expectedSignature = CreateSignature(data);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(parts[2]),
                Encoding.ASCII.GetBytes(expectedSignature)))
        {
            return false;
        }

        try
        {
            using var header = JsonDocument.Parse(Base64Url.Decode(parts[0]));
            if (!TryGetString(header.RootElement, "alg", out var algorithm) || algorithm != "HS256")
            {
                return false;
            }

            using var payload = JsonDocument.Parse(Base64Url.Decode(parts[1]));
            var root = payload.RootElement;

            if (!TryGetString(root, "iss", out var issuer) || issuer != _options.Issuer)
            {
                return false;
            }

            if (!TryGetString(root, "aud", out var audience) || audience != _options.Audience)
            {
                return false;
            }

            if (!TryGetString(root, "sub", out var subject) || !Guid.TryParse(subject, out _))
            {
                return false;
            }

            if (!TryGetLong(root, "nbf", out var notBefore) ||
                DateTimeOffset.FromUnixTimeSeconds(notBefore) > DateTimeOffset.UtcNow.Add(ClockSkew))
            {
                return false;
            }

            if (!TryGetLong(root, "exp", out var expiresAt) ||
                DateTimeOffset.FromUnixTimeSeconds(expiresAt) < DateTimeOffset.UtcNow.Subtract(ClockSkew))
            {
                return false;
            }

            TryGetString(root, "email", out var email);
            var emailVerified = TryGetBoolean(root, "email_verified", out var verified) && verified;
            var hasPartner = TryGetBoolean(root, "has_partner", out var partner) && partner;

            var claims = new List<Claim>
            {
                new("sub", subject),
                new(ClaimTypes.NameIdentifier, subject),
                new("email_verified", emailVerified.ToString()),
                new("has_partner", hasPartner.ToString())
            };

            if (!string.IsNullOrWhiteSpace(email))
            {
                claims.Add(new Claim(ClaimTypes.Email, email));
            }

            principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string CreateSignature(string data)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var signature = hmac.ComputeHash(Encoding.ASCII.GetBytes(data));
        return Base64Url.Encode(signature);
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryGetLong(JsonElement element, string propertyName, out long value)
    {
        value = 0;
        return element.TryGetProperty(propertyName, out var property) && property.TryGetInt64(out value);
    }

    private static bool TryGetBoolean(JsonElement element, string propertyName, out bool value)
    {
        value = false;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return false;
        }

        value = property.GetBoolean();
        return true;
    }
}
