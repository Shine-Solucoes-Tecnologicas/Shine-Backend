using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Shine.Shared;

namespace Shine.Infrastructure;

public sealed class JwtOptions
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "Shine";
    public string Audience { get; init; } = "Shine.Api";
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IAccessTokenService
{
    AccessTokenResult Create(Guid userId, Guid? tenantId, Guid? userTenantId, IEnumerable<string> roles);
}

public sealed class HmacAccessTokenService(IOptions<JwtOptions> options) : IAccessTokenService
{
    private readonly JwtOptions settings = options.Value;

    public AccessTokenResult Create(Guid userId, Guid? tenantId, Guid? userTenantId, IEnumerable<string> roles)
    {
        if (string.IsNullOrWhiteSpace(settings.Secret)) throw new InvalidOperationException("Jwt:Secret must be configured.");
        var expires = DateTime.UtcNow.AddMinutes(settings.AccessTokenMinutes);
        var header = Base64UrlEncoding.Encode(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var payload = new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["user_id"] = userId,
            ["iss"] = settings.Issuer,
            ["aud"] = settings.Audience,
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(expires).ToUnixTimeSeconds(),
            ["roles"] = roles.ToArray()
        };
        if (tenantId is not null) payload["tenant_id"] = tenantId;
        if (userTenantId is not null) payload["user_tenant_id"] = userTenantId;
        var body = Base64UrlEncoding.Encode(JsonSerializer.SerializeToUtf8Bytes(payload));
        var unsigned = $"{header}.{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(settings.Secret));
        var signature = Base64UrlEncoding.Encode(hmac.ComputeHash(Encoding.UTF8.GetBytes(unsigned)));
        return new AccessTokenResult($"{unsigned}.{signature}", expires);
    }

}

public static class RefreshTokenHash
{
    public static (string Raw, string Hash) Create()
    {
        var raw = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (raw, Hash(raw));
    }

    public static string Hash(string raw) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
