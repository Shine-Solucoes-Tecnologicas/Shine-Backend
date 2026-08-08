using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.Api;

public sealed class JwtAuthenticationMiddleware(RequestDelegate next, IOptions<JwtOptions> options, ILogger<JwtAuthenticationMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var token = context.Request.Headers.Authorization.FirstOrDefault();
        if (token?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true &&
            TryValidate(token[7..], options.Value, out var principal))
            context.User = principal;

        await next(context);
    }

    private bool TryValidate(string token, JwtOptions settings, out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal(new ClaimsIdentity());
        var parts = token.Split('.');
        if (parts.Length != 3 || string.IsNullOrWhiteSpace(settings.Secret)) return false;
        try
        {
            var unsigned = $"{parts[0]}.{parts[1]}";
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.Secret));
            var expected = Base64Url(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(parts[2]))) return false;
            using var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64UrlDecode(parts[1])));
            var root = payload.RootElement;
            if (root.GetProperty("iss").GetString() != settings.Issuer || root.GetProperty("aud").GetString() != settings.Audience ||
                root.GetProperty("exp").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            var claims = new List<Claim>();
            AddClaim(root, claims, "sub", ClaimTypes.NameIdentifier);
            AddClaim(root, claims, "user_id", "user_id");
            AddClaim(root, claims, "tenant_id", "tenant_id");
            AddClaim(root, claims, "user_tenant_id", "user_tenant_id");
            if (root.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
                foreach (var role in roles.EnumerateArray()) claims.Add(new Claim(ClaimTypes.Role, role.GetString() ?? string.Empty));
            principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or OverflowException)
        {
            logger.LogDebug(ex, "Invalid bearer token.");
            return false;
        }
    }

    private static void AddClaim(JsonElement root, List<Claim> claims, string jsonName, string claimType)
    {
        if (root.TryGetProperty(jsonName, out var value)) claims.Add(new Claim(claimType, value.ToString()));
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[] Base64UrlDecode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4));
}
