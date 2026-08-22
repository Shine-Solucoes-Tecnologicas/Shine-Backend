using System.Security.Cryptography;
using System.Text;

namespace Shine.Api;

internal static class PasswordResetTokenService
{
    public static (string RawToken, string TokenHash) Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(bytes);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return (rawToken, Convert.ToHexString(hash));
    }

    public static string Hash(string rawToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash);
    }
}
