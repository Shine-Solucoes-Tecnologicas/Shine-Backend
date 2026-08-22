using Shine.Domain;

namespace Shine.UnitTests;

public sealed class DateTimePolicyTests
{
    [Fact]
    public void Converts_utc_instant_using_tenant_timezone()
    {
        var utc = new DateTime(2026, 1, 15, 15, 0, 0, DateTimeKind.Utc);

        var local = DateTimePolicy.ToTenantTime(utc, "America/Sao_Paulo");

        Assert.Equal(new DateTime(2026, 1, 15, 12, 0, 0), local);
        Assert.Equal(DateTimeKind.Unspecified, local.Kind);
    }

    [Fact]
    public void Normalizes_unspecified_values_as_utc()
    {
        var value = new DateTime(2026, 1, 15, 15, 0, 0, DateTimeKind.Unspecified);

        var normalized = DateTimePolicy.EnsureUtc(value);

        Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        Assert.Equal(value, normalized);
    }
}
