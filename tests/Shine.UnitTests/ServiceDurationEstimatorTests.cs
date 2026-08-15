using Scheduling.Application;
using Scheduling.Domain;

namespace Shine.UnitTests;

public sealed class ServiceDurationEstimatorTests
{
    [Fact]
    public void Uses_fixed_service_duration_when_no_rule_applies()
    {
        var estimator = new ServiceDurationEstimator([]);
        var estimate = estimator.Estimate(Context(), 45);

        Assert.Equal(45, estimate.DurationMinutes);
        Assert.Equal("fixed-v1", estimate.RuleVersion);
    }

    [Fact]
    public void Uses_the_single_applicable_rule()
    {
        var estimator = new ServiceDurationEstimator([new AttributeRule("hair-length", "long", 90)]);
        var estimate = estimator.Estimate(Context(("hair-length", "long")), 45);

        Assert.Equal(90, estimate.DurationMinutes);
        Assert.Equal("attribute-v1", estimate.RuleVersion);
    }

    [Fact]
    public void Rejects_ambiguous_rules()
    {
        var estimator = new ServiceDurationEstimator([
            new AttributeRule("hair-length", "long", 90),
            new AttributeRule("hair-length", "long", 120)]);

        Assert.Throws<InvalidOperationException>(() => estimator.Estimate(Context(("hair-length", "long")), 45));
    }

    [Fact]
    public void Rejects_invalid_base_duration()
    {
        var estimator = new ServiceDurationEstimator([]);

        Assert.Throws<ArgumentOutOfRangeException>(() => estimator.Estimate(Context(), 0));
    }

    private static ServiceDurationContext Context(params (string Key, string Value)[] attributes) =>
        new(Guid.NewGuid(), Guid.NewGuid(), null, attributes.ToDictionary(x => x.Key, x => x.Value));

    private sealed class AttributeRule(string key, string value, int duration) : IServiceDurationRule
    {
        public string Version => "attribute-v1";
        public bool CanResolve(ServiceDurationContext context) => context.Attributes.TryGetValue(key, out var actual) && actual == value;
        public ServiceDurationEstimate Resolve(ServiceDurationContext context, int baseDurationMinutes) =>
            new(duration, Math.Min(baseDurationMinutes, duration), Math.Max(baseDurationMinutes, duration), Version, $"Attribute '{key}' matched.");
    }
}
