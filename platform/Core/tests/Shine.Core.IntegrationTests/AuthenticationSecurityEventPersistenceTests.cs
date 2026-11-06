using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class AuthenticationSecurityEventPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Emitter_routes_the_same_safe_envelope_to_operational_log_and_audit()
    {
        await using var db = fixture.CreateDb();
        var emitter = CreateEmitter(db);
        var actorId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var correlation = $"security-{Guid.NewGuid():N}";

        emitter.Enqueue(new(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.Unknown, correlation, actorId, actorId, TenantId: tenantId,
            SubjectIdentifier: "sensitive@example.test", NetworkAddress: "198.51.100.9",
            UserAgent: "Mozilla/5.0 Chrome/125.0 SecretAgentMarker"));
        await db.SaveChangesAsync();

        var log = await db.OperationalLogs.SingleAsync(x => x.CorrelationId == correlation);
        var audit = await db.AuditEntries.SingleAsync(x => x.CorrelationId == correlation && x.EntityType == "AuthenticationSecurityEvent");
        Assert.Equal(log.Message, audit.NewValuesJson);
        Assert.Contains(AuthenticationSecurityEventNames.Login, log.Message);
        Assert.Contains(AuthenticationSecurityOutcomes.Succeeded, log.Message);
        Assert.DoesNotContain("sensitive@example.test", log.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("198.51.100.9", log.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretAgentMarker", log.Message, StringComparison.Ordinal);
        Assert.Null(audit.IpAddress);
        Assert.Null(audit.UserAgent);
    }

    [Fact]
    public async Task Routing_keeps_high_volume_denials_out_of_audit_and_persists_material_events()
    {
        await using var db = fixture.CreateDb();
        var emitter = CreateEmitter(db);
        var deniedCorrelation = $"denied-{Guid.NewGuid():N}";
        var refreshCorrelation = $"refresh-{Guid.NewGuid():N}";
        var reuseCorrelation = $"reuse-{Guid.NewGuid():N}";

        emitter.Enqueue(new(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Denied,
            AuthenticationSecurityReasons.InvalidCredentials, deniedCorrelation, SubjectIdentifier: "unknown@example.test"));
        emitter.Enqueue(new(AuthenticationSecurityEventNames.SessionRefresh, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.Unknown, refreshCorrelation, SessionId: Guid.NewGuid()));
        emitter.Enqueue(new(AuthenticationSecurityEventNames.SessionReuseDetected, AuthenticationSecurityOutcomes.Detected,
            AuthenticationSecurityReasons.TokenReuseDetected, reuseCorrelation, SessionId: Guid.NewGuid()));
        await db.SaveChangesAsync();

        Assert.Equal(3, await db.OperationalLogs.CountAsync(x =>
            x.CorrelationId == deniedCorrelation || x.CorrelationId == refreshCorrelation || x.CorrelationId == reuseCorrelation));
        Assert.False(await db.AuditEntries.AnyAsync(x => x.CorrelationId == deniedCorrelation));
        Assert.False(await db.AuditEntries.AnyAsync(x => x.CorrelationId == refreshCorrelation));
        Assert.True(await db.AuditEntries.AnyAsync(x => x.CorrelationId == reuseCorrelation && x.EntityType == "AuthenticationSecurityEvent"));
    }

    private static AuthenticationSecurityEventEmitter CreateEmitter(Shine.Infrastructure.Persistence.ShineDbContext db)
    {
        var builder = new AuthenticationSecurityEventBuilder(Options.Create(new SecurityFingerprintOptions
        {
            ActiveKeyVersion = "test-v1",
            Keys = [new SecurityFingerprintKeyOptions
            {
                Version = "test-v1",
                Secret = "Dedicated.Security.Fingerprint.Test.Secret.Key.2026"
            }]
        }));
        return new(db, builder);
    }
}
