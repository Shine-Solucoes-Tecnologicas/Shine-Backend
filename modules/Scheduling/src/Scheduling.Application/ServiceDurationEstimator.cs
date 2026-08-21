using Scheduling.Domain;

namespace Scheduling.Application;

/// <summary>Resolves a service duration using the first applicable deterministic rule.</summary>
public sealed class ServiceDurationEstimator(IEnumerable<IServiceDurationRule> rules)
{
    private readonly IReadOnlyCollection<IServiceDurationRule> rules = rules?.ToArray()
        ?? throw new ArgumentNullException(nameof(rules));

    public ServiceDurationEstimate Estimate(ServiceDurationContext context, int baseDurationMinutes)
    {
        ArgumentNullException.ThrowIfNull(context);
        ValidateBaseDuration(baseDurationMinutes);

        var applicable = rules.Where(rule => rule.CanResolve(context)).ToArray();
        if (applicable.Length > 1)
            throw new InvalidOperationException("More than one service duration rule applies to the same context.");

        if (applicable.Length == 1)
            return applicable[0].Resolve(context, baseDurationMinutes);

        return new ServiceDurationEstimate(
            baseDurationMinutes,
            baseDurationMinutes,
            baseDurationMinutes,
            "fixed-v1",
            "Base service duration.");
    }

    private static void ValidateBaseDuration(int durationMinutes)
    {
        if (durationMinutes <= 0 || durationMinutes > 1440)
            throw new ArgumentOutOfRangeException(nameof(durationMinutes));
    }
}

public sealed class ConfiguredAttributeDurationRule : IServiceDurationRule
{
    public string Version => "configured-attribute-v1";
    public bool CanResolve(ServiceDurationContext context) => context.Policy is not null;

    public ServiceDurationEstimate Resolve(ServiceDurationContext context, int baseDurationMinutes)
    {
        var policy = context.Policy!.Validate();
        if (!context.Attributes.TryGetValue(policy.AttributeKey, out var raw) || !int.TryParse(raw, out var units) || units < 0)
            throw new ArgumentException($"Duration attribute '{policy.AttributeKey}' is required and must be a non-negative integer.", nameof(context));
        var calculated = (long)baseDurationMinutes + (long)units * policy.MinutesPerUnit;
        var duration = (int)Math.Clamp(calculated, policy.MinimumMinutes, policy.MaximumMinutes);
        return new ServiceDurationEstimate(duration, policy.MinimumMinutes, policy.MaximumMinutes, policy.Version,
            $"Estimated from '{policy.AttributeKey}' with {units} unit(s).");
    }
}
