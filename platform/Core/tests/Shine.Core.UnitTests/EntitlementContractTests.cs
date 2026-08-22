using Shine.Domain;

namespace Shine.UnitTests;

public sealed class EntitlementContractTests
{
    [Fact]
    public void ModuleCodesAreNormalized()
    {
        var snapshot = new EntitlementSnapshot(Guid.NewGuid(), new HashSet<string> { "SCHEDULING" }, new Dictionary<string, long>());
        Assert.True(snapshot.HasModule(" scheduling "));
    }

    [Fact]
    public void MissingLimitIsDeniedUntilDefined()
    {
        var snapshot = new EntitlementSnapshot(Guid.NewGuid(), new HashSet<string>(), new Dictionary<string, long>());
        Assert.False(snapshot.Allows("LIMIT_A", 1));
    }

    [Fact]
    public void DefinedLimitRejectsRequestsAboveMaximum()
    {
        var snapshot = new EntitlementSnapshot(Guid.NewGuid(), new HashSet<string>(), new Dictionary<string, long>
        {
            ["LIMIT_A"] = 5
        });
        Assert.True(snapshot.Allows("LIMIT_A", 5));
        Assert.False(snapshot.Allows("LIMIT_A", 6));
    }

    [Fact]
    public void Revoked_or_expired_entitlement_is_denied()
    {
        var revoked = new EntitlementGrant("LIMIT_A", 10, "plan", EntitlementState.Revoked);
        var expired = new EntitlementGrant("LIMIT_B", 10, "plan", expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        var snapshot = new EntitlementSnapshot(Guid.NewGuid(), new HashSet<string>(), new Dictionary<string, EntitlementGrant> { [revoked.Key] = revoked, [expired.Key] = expired });
        Assert.False(snapshot.Allows("LIMIT_A"));
        Assert.False(snapshot.Allows("LIMIT_B"));
    }

    [Fact]
    public void Limit_decision_distinguishes_missing_unlimited_available_and_exhausted()
    {
        var unlimited = new EntitlementGrant("UNLIMITED", 0, "plan", isUnlimited: true);
        var limited = new EntitlementGrant("LIMITED", 5, "plan");
        var snapshot = new EntitlementSnapshot(Guid.NewGuid(), new HashSet<string>(), new Dictionary<string, EntitlementGrant>
        {
            [unlimited.Key] = unlimited,
            [limited.Key] = limited
        });

        Assert.Equal(EntitlementLimitStatus.NotConfigured, snapshot.EvaluateLimit("MISSING", 0).Status);
        Assert.Equal(EntitlementLimitStatus.Unlimited, snapshot.EvaluateLimit("UNLIMITED", 1_000_000).Status);
        var available = snapshot.EvaluateLimit("LIMITED", 2, 2);
        Assert.True(available.Allowed);
        Assert.Equal(1, available.Remaining);
        var exhausted = snapshot.EvaluateLimit("LIMITED", 4, 2);
        Assert.False(exhausted.Allowed);
        Assert.Equal(EntitlementLimitStatus.Exhausted, exhausted.Status);
        Assert.Equal(1, exhausted.Remaining);
    }

    [Fact]
    public void Technical_catalog_exposes_only_registered_limit_keys()
    {
        var catalog = new EntitlementKeyCatalog();

        Assert.True(catalog.Contains(EntitlementKeys.SchedulingActiveAppointments));
        Assert.True(catalog.Contains(" appointments.max "));
        Assert.True(catalog.Contains(EntitlementKeys.SchedulingProfessionals));
        Assert.True(catalog.Contains(EntitlementKeys.SchedulingServices));
        Assert.False(catalog.Contains("BUSINESS.VALUE.NOT.REGISTERED"));
    }
}
