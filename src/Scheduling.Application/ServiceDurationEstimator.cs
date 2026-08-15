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
