using System.Security.Cryptography;
using System.Text;

namespace Petty.Server.Application.Features.Auth;

public static class RefreshTokens
{
    public static string Create(Guid sessionId) => $"{sessionId:N}.{Convert.ToHexString(RandomNumberGenerator.GetBytes(32))}";

    public static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    public static bool TryGetSessionId(string? token, out Guid sessionId)
    {
        sessionId = default;
        return token is { Length: 97 } && token[32] == '.' && Guid.TryParseExact(token.AsSpan(0, 32), "N", out sessionId)
            && token.AsSpan(33).IndexOfAnyExcept("0123456789ABCDEF") < 0;
    }

    public static bool Matches(string token, string expectedHash) => CryptographicOperations.FixedTimeEquals(
        SHA256.HashData(Encoding.UTF8.GetBytes(token)), Convert.FromHexString(expectedHash));
}
