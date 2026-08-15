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
    public void MissingLimitIsUnlimitedUntilDefined()
    {
        var snapshot = new EntitlementSnapshot(Guid.NewGuid(), new HashSet<string>(), new Dictionary<string, long>());
        Assert.True(snapshot.Allows("LIMIT_A", 1000));
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
}
