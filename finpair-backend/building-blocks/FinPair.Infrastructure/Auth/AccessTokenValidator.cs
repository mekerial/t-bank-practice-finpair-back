using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace FinPair.Infrastructure.Auth;

public sealed class AccessTokenValidator
{
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly AccessTokenOptions _options;
    private readonly byte[] _signingKey;

    public AccessTokenValidator(IOptions<AccessTokenOptions> options)
    {
        _options = options.Value;
        _options.Validate();
        _signingKey = Encoding.UTF8.GetBytes(_options.SigningKey);
    }

    public bool TryValidate(string token, out ClaimsPrincipal principal)
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
