using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Shine.Infrastructure;

public sealed class JwtOptions
{
    public string Secret { get; init; } = string.Empty;
    public string Issuer { get; init; } = "Shine";
    public string Audience { get; init; } = "Shine.Api";
    public int AccessTokenMinutes { get; init; } = 15;
    public int RefreshTokenDays { get; init; } = 30;
    public int ClockSkewSeconds { get; init; } = 30;
    public string ActiveKeyId { get; init; } = string.Empty;
    public List<JwtSigningKeyOptions> SigningKeys { get; init; } = [];
}

public sealed class JwtSigningKeyOptions
{
    public string Id { get; init; } = string.Empty;
    public string Secret { get; init; } = string.Empty;
}

public sealed class JwtSigningKeyRing
{
    private const string LegacyKeyId = "legacy";
    private readonly IReadOnlyDictionary<string, SymmetricSecurityKey> keys;

    public JwtSigningKeyRing(IOptions<JwtOptions> options)
    {
        var settings = options.Value;
        if (!TryValidate(settings, out var error))
            throw new OptionsValidationException(nameof(JwtOptions), typeof(JwtOptions), [error]);

        var definitions = Definitions(settings);
        keys = definitions.ToDictionary(
            definition => definition.Id,
            definition => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(definition.Secret)) { KeyId = definition.Id },
            StringComparer.Ordinal);
        var activeKeyId = settings.SigningKeys.Count == 0 ? LegacyKeyId : settings.ActiveKeyId;
        ActiveKey = keys[activeKeyId];
    }

    public SymmetricSecurityKey ActiveKey { get; }
    public IReadOnlyCollection<SecurityKey> ValidationKeys => keys.Values.Cast<SecurityKey>().ToArray();

    public IEnumerable<SecurityKey> Resolve(string? keyId) => string.IsNullOrWhiteSpace(keyId)
        ? ValidationKeys
        : keys.TryGetValue(keyId, out var key) ? [key] : [];

    public static bool TryValidate(JwtOptions settings, out string error)
    {
        if (string.IsNullOrWhiteSpace(settings.Issuer) || string.IsNullOrWhiteSpace(settings.Audience))
        {
            error = "Jwt issuer and audience must be configured.";
            return false;
        }
        if (settings.AccessTokenMinutes <= 0 || settings.RefreshTokenDays <= 0 || settings.ClockSkewSeconds is < 0 or > 300)
        {
            error = "JWT lifetimes must be positive and clock skew must be between 0 and 300 seconds.";
            return false;
        }

        var definitions = Definitions(settings);
        if (definitions.Count == 0 || definitions.Any(key => string.IsNullOrWhiteSpace(key.Id) || Encoding.UTF8.GetByteCount(key.Secret) < 32))
        {
            error = "Every JWT signing key must have an id and at least 32 bytes of secret material.";
            return false;
        }
        if (definitions.Select(key => key.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
        {
            error = "JWT signing key ids must be unique.";
            return false;
        }
        if (settings.SigningKeys.Count > 0 && !definitions.Any(key => key.Id == settings.ActiveKeyId))
        {
            error = "Jwt:ActiveKeyId must identify one of Jwt:SigningKeys.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static IReadOnlyList<JwtSigningKeyOptions> Definitions(JwtOptions settings) => settings.SigningKeys.Count > 0
        ? settings.SigningKeys
        : string.IsNullOrWhiteSpace(settings.Secret)
            ? []
            : [new JwtSigningKeyOptions { Id = LegacyKeyId, Secret = settings.Secret }];
}

public static class JwtTokenValidation
{
    public static TokenValidationParameters Create(JwtOptions settings, JwtSigningKeyRing keyRing) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = settings.Issuer,
        ValidateAudience = true,
        ValidAudience = settings.Audience,
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        RequireExpirationTime = true,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromSeconds(settings.ClockSkewSeconds),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        NameClaimType = ClaimTypes.NameIdentifier,
        RoleClaimType = ClaimTypes.Role,
        IssuerSigningKeyResolver = (_, _, keyId, _) => keyRing.Resolve(keyId)
    };
}

public sealed record AccessTokenResult(string Token, DateTime ExpiresAtUtc);

public interface IAccessTokenService
{
    AccessTokenResult Create(Guid userId, Guid? tenantId, Guid? userTenantId, IEnumerable<string> roles);
}

public sealed class JwtAccessTokenService(IOptions<JwtOptions> options, JwtSigningKeyRing keyRing) : IAccessTokenService
{
    private readonly JwtOptions settings = options.Value;
    private readonly JwtSecurityTokenHandler handler = new();

    public AccessTokenResult Create(Guid userId, Guid? tenantId, Guid? userTenantId, IEnumerable<string> roles)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(settings.AccessTokenMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new("user_id", userId.ToString()),
            new(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        if (tenantId is Guid effectiveTenantId) claims.Add(new Claim("tenant_id", effectiveTenantId.ToString()));
        if (userTenantId is Guid effectiveUserTenantId) claims.Add(new Claim("user_tenant_id", effectiveUserTenantId.ToString()));
        claims.AddRange(roles.Distinct(StringComparer.OrdinalIgnoreCase).Select(role => new Claim(ClaimTypes.Role, role)));

        var token = new JwtSecurityToken(
            issuer: settings.Issuer,
            audience: settings.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expires.UtcDateTime,
            signingCredentials: new SigningCredentials(keyRing.ActiveKey, SecurityAlgorithms.HmacSha256));
        return new AccessTokenResult(handler.WriteToken(token), expires.UtcDateTime);
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
