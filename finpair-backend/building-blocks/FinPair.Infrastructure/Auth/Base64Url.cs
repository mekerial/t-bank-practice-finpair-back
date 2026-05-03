using System.Buffers.Text;

namespace FinPair.Infrastructure.Auth;

internal static class Base64Url
{
    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes.ToArray())
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static byte[] Decode(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        var padding = (4 - base64.Length % 4) % 4;
        return Convert.FromBase64String(base64 + new string('=', padding));
    }
}
