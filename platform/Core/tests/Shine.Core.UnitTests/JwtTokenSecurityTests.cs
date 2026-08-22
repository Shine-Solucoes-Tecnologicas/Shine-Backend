using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class JwtTokenSecurityTests
{
    private const string OldSecret = "old-signing-secret-with-at-least-32-bytes-2026";
    private const string NewSecret = "new-signing-secret-with-at-least-32-bytes-2026";
    private const string Hs512Secret = "unexpected-hs512-signing-secret-with-at-least-sixty-four-bytes-2026-08";

    [Fact]
    public async Task Issued_token_has_kid_and_preserves_identity_tenant_and_role_claims()
    {
        var settings = Settings("current", 30, ("current", NewSecret));
        var options = Options.Create(settings);
        var ring = new JwtSigningKeyRing(options);
        var service = new JwtAccessTokenService(options, ring);
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var userTenantId = Guid.NewGuid();

        var result = service.Create(userId, tenantId, userTenantId, ["editor", "financial"]);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal("current", token.Header.Kid);
        Assert.Equal(SecurityAlgorithms.HmacSha256, token.Header.Alg);
        Assert.Equal(userId.ToString(), token.Subject);
        Assert.Contains(token.Claims, claim => claim.Type == "user_id" && claim.Value == userId.ToString());
        Assert.Contains(token.Claims, claim => claim.Type == "tenant_id" && claim.Value == tenantId.ToString());
        Assert.Contains(token.Claims, claim => claim.Type == "user_tenant_id" && claim.Value == userTenantId.ToString());
        Assert.True((await Validate(result.Token, settings, ring)).IsValid);
    }

    [Fact]
    public async Task Validator_rejects_invalid_issuer_audience_key_algorithm_expiration_and_malformed_token()
    {
        var settings = Settings("current", 0, ("current", NewSecret));
        var ring = new JwtSigningKeyRing(Options.Create(settings));
        var now = DateTime.UtcNow;

        Assert.False((await Validate(Token("other", settings.Audience, "current", NewSecret, SecurityAlgorithms.HmacSha256, now.AddMinutes(5)), settings, ring)).IsValid);
        Assert.False((await Validate(Token(settings.Issuer, "other", "current", NewSecret, SecurityAlgorithms.HmacSha256, now.AddMinutes(5)), settings, ring)).IsValid);
        Assert.False((await Validate(Token(settings.Issuer, settings.Audience, "current", OldSecret, SecurityAlgorithms.HmacSha256, now.AddMinutes(5)), settings, ring)).IsValid);
        Assert.False((await Validate(Token(settings.Issuer, settings.Audience, "unknown", NewSecret, SecurityAlgorithms.HmacSha256, now.AddMinutes(5)), settings, ring)).IsValid);
        Assert.False((await Validate(Token(settings.Issuer, settings.Audience, "current", Hs512Secret, SecurityAlgorithms.HmacSha512, now.AddMinutes(5)), settings, ring)).IsValid);
        Assert.False((await Validate(Token(settings.Issuer, settings.Audience, "current", NewSecret, SecurityAlgorithms.HmacSha256, now.AddMinutes(-1), now.AddMinutes(-5)), settings, ring)).IsValid);
        Assert.False((await Validate("not-a-jwt", settings, ring)).IsValid);
    }

    [Fact]
    public async Task Rotation_accepts_previous_key_until_it_is_removed_and_emits_only_with_active_key()
    {
        var rotating = Settings("new", 30, ("old", OldSecret), ("new", NewSecret));
        var rotatingOptions = Options.Create(rotating);
        var rotatingRing = new JwtSigningKeyRing(rotatingOptions);
        var oldToken = Token(rotating.Issuer, rotating.Audience, "old", OldSecret, SecurityAlgorithms.HmacSha256, DateTime.UtcNow.AddMinutes(5));
        var newToken = new JwtAccessTokenService(rotatingOptions, rotatingRing)
            .Create(Guid.NewGuid(), null, null, []).Token;

        Assert.True((await Validate(oldToken, rotating, rotatingRing)).IsValid);
        Assert.Equal("new", new JwtSecurityTokenHandler().ReadJwtToken(newToken).Header.Kid);

        var retired = Settings("new", 30, ("new", NewSecret));
        var retiredRing = new JwtSigningKeyRing(Options.Create(retired));
        Assert.False((await Validate(oldToken, retired, retiredRing)).IsValid);
        Assert.True((await Validate(newToken, retired, retiredRing)).IsValid);
    }

    private static Task<TokenValidationResult> Validate(string token, JwtOptions settings, JwtSigningKeyRing ring) =>
        new JsonWebTokenHandler().ValidateTokenAsync(token, JwtTokenValidation.Create(settings, ring));

    private static string Token(
        string issuer,
        string audience,
        string keyId,
        string secret,
        string algorithm,
        DateTime expires,
        DateTime? notBefore = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)) { KeyId = keyId };
        var token = new JwtSecurityToken(
            issuer,
            audience,
            [new Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, Guid.NewGuid().ToString())],
            notBefore ?? DateTime.UtcNow.AddMinutes(-1),
            expires,
            new SigningCredentials(key, algorithm));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static JwtOptions Settings(string activeKeyId, int clockSkewSeconds, params (string Id, string Secret)[] keys) => new()
    {
        Issuer = "Shine",
        Audience = "Shine.Api",
        ActiveKeyId = activeKeyId,
        ClockSkewSeconds = clockSkewSeconds,
        SigningKeys = keys.Select(key => new JwtSigningKeyOptions { Id = key.Id, Secret = key.Secret }).ToList()
    };
}
