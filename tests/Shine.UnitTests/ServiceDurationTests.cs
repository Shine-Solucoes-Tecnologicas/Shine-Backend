using Scheduling.Domain;

namespace Shine.UnitTests;

public sealed class ServiceDurationTests
{
    [Fact]
    public void Context_normalizes_attribute_keys_and_values()
    {
        var context = new ServiceDurationContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            new Dictionary<string, string> { ["  hair-length "] = " long " });

        Assert.Equal("long", context.Attributes["hair-length"]);
    }

    [Fact]
    public void Context_rejects_empty_identity_or_attributes()
    {
        Assert.Throws<ArgumentException>(() => new ServiceDurationContext(Guid.Empty, Guid.NewGuid(), null, new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new ServiceDurationContext(Guid.NewGuid(), Guid.NewGuid(), null, new Dictionary<string, string> { [" "] = "long" }));
    }

    [Fact]
    public void Estimate_requires_valid_limits_and_reason()
    {
        var estimate = new ServiceDurationEstimate(60, 30, 90, "v1", "base duration");

        Assert.Equal(60, estimate.DurationMinutes);
        Assert.Throws<ArgumentException>(() => new ServiceDurationEstimate(120, 30, 90, "v1", "invalid"));
        Assert.Throws<ArgumentException>(() => new ServiceDurationEstimate(60, 30, 90, "v1", " "));
    }
}
