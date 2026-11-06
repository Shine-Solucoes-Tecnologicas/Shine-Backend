using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shine.Api;
using Shine.Api.Controllers;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class TokenConsumptionConcurrencyTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Concurrent_refresh_rotation_allows_exactly_one_success()
    {
        var user = new User($"refresh-race-{Guid.NewGuid():N}@example.test", "hash");
        var source = RefreshTokenHash.Create();
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, new RefreshToken(user.Id, null, null, source.Hash, DateTime.UtcNow.AddDays(1)));
            await setup.SaveChangesAsync();
        }

        async Task<ActionResult<RefreshResponse>> RotateAsync()
        {
            await using var db = fixture.CreateDb();
            return await Authentication(db).Refresh(new RefreshRequest(source.Raw), default);
        }

        var results = await Task.WhenAll(RotateAsync(), RotateAsync());

        var success = Assert.Single(results, x => x.Result is OkObjectResult);
        Assert.Single(results, x => x.Result is ObjectResult { StatusCode: 401 });
        var response = Assert.IsType<RefreshResponse>(Assert.IsType<OkObjectResult>(success.Result).Value);
        await using var verification = fixture.CreateDb();
        var stored = await verification.RefreshTokens.Where(x => x.UserId == user.Id).ToArrayAsync();
        Assert.Equal(2, stored.Length);
        Assert.Single(stored, x => x.TokenHash == RefreshTokenHash.Hash(response.RefreshToken!));
        Assert.DoesNotContain(stored, x => x.TokenHash == response.RefreshToken);
        var evidence = await verification.OperationalLogs.AsNoTracking()
            .Where(x => x.Message.Contains(user.Id.ToString()) && x.Message.Contains(AuthenticationSecurityEventNames.SessionRefresh))
            .Select(x => x.Message)
            .ToArrayAsync();
        Assert.Single(evidence, x => x.Contains($"\"outcome\":\"{AuthenticationSecurityOutcomes.Succeeded}\""));
        Assert.DoesNotContain(evidence, x => x.Contains(source.Raw, StringComparison.Ordinal));
        Assert.DoesNotContain(evidence, x => x.Contains(response.RefreshToken!, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Concurrent_password_reset_allows_exactly_one_success()
    {
        var user = new User($"reset-race-{Guid.NewGuid():N}@example.test", "old-hash");
        var rawToken = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var tokenHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(rawToken)));
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, new PasswordResetToken(user.Id, tokenHash, DateTime.UtcNow.AddMinutes(30)));
            await setup.SaveChangesAsync();
        }

        async Task<IActionResult> ResetAsync()
        {
            await using var db = fixture.CreateDb();
            return await PasswordRecovery(db).Reset(new PasswordResetRequest(rawToken, "New-password-123!"), default);
        }

        var results = await Task.WhenAll(ResetAsync(), ResetAsync());

        Assert.Single(results, x => x is NoContentResult);
        Assert.Single(results, x => x is ObjectResult { StatusCode: 400 });
        await using var verification = fixture.CreateDb();
        Assert.NotNull((await verification.PasswordResetTokens.SingleAsync(x => x.UserId == user.Id)).UsedAtUtc);
        Assert.Equal("hashed:New-password-123!", (await verification.Users.SingleAsync(x => x.Id == user.Id)).PasswordHash);
        var evidence = await verification.OperationalLogs.AsNoTracking()
            .Where(x => x.Message.Contains(user.Id.ToString()) && x.Message.Contains(AuthenticationSecurityEventNames.PasswordResetCompleted))
            .Select(x => x.Message)
            .ToArrayAsync();
        Assert.Single(evidence, x => x.Contains($"\"outcome\":\"{AuthenticationSecurityOutcomes.Succeeded}\""));
        Assert.DoesNotContain(evidence, x => x.Contains(rawToken, StringComparison.Ordinal));
        Assert.DoesNotContain(evidence, x => x.Contains("New-password-123!", StringComparison.Ordinal));
    }

    private static AuthenticationController Authentication(Shine.Infrastructure.Persistence.ShineDbContext db)
    {
        var accessTokens = new TestAccessTokens();
        var jwt = Options.Create(new JwtOptions { RefreshTokenDays = 30 });
        var events = SecurityEvents(db);
        var tenantSelection = new TenantSessionSelectionService(
            db, accessTokens, jwt, events, new HttpContextAccessor());
        return new AuthenticationController(
            db, new TestPasswordHash(), new TestPasswordPolicy(), accessTokens,
            jwt, Options.Create(new LoginSecurityOptions()), events,
            new LegacyTestWebSessionTransport(), tenantSelection);
    }

    private static PasswordRecoveryController PasswordRecovery(Shine.Infrastructure.Persistence.ShineDbContext db) =>
        new(db, new TestPasswordHash(), new TestPasswordPolicy(), new TestTemplate(), new NullPasswordRecoveryDelivery(),
            NullLogger<PasswordRecoveryController>.Instance, SecurityEvents(db), new LegacyTestWebSessionTransport());

    private static IAuthenticationSecurityEventEmitter SecurityEvents(Shine.Infrastructure.Persistence.ShineDbContext db) =>
        new AuthenticationSecurityEventEmitter(db, new AuthenticationSecurityEventBuilder(Options.Create(new SecurityFingerprintOptions
        {
            ActiveKeyVersion = "test",
            Keys = [new SecurityFingerprintKeyOptions { Version = "test", Secret = "integration-security-fingerprint-key-32-bytes" }]
        })));

    private sealed class TestPasswordHash : IPasswordHashService
    {
        public string Hash(string password) => $"hashed:{password}";
        public bool Verify(string password, string? encodedHash) => encodedHash == Hash(password);
    }

    private sealed class TestPasswordPolicy : IPasswordPolicy
    {
        public bool IsValid(string password, out IReadOnlyCollection<string> errors)
        {
            errors = [];
            return true;
        }
    }

    private sealed class TestAccessTokens : IAccessTokenService
    {
        public AccessTokenResult Create(Guid userId, Guid sessionId, Guid? tenantId, Guid? userTenantId, IEnumerable<string> roles) =>
            new("access-token", DateTime.UtcNow.AddMinutes(5));
    }

    private sealed class TestTemplate : IPasswordRecoveryMessageTemplate
    {
        public PasswordRecoveryMessage Create(string email, string rawToken, DateTime expiresAtUtc) => new("", "", "");
    }
}
