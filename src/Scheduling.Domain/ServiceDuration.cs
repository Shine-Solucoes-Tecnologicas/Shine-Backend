namespace Scheduling.Domain;

/// <summary>Context used to resolve duration without coupling Scheduling to a UI or provider.</summary>
public sealed record ServiceDurationContext
{
    public ServiceDurationContext(Guid tenantId, Guid serviceId, Guid? professionalId, IReadOnlyDictionary<string, string> attributes)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (serviceId == Guid.Empty) throw new ArgumentException("Service is required.", nameof(serviceId));
        TenantId = tenantId;
        ServiceId = serviceId;
        ProfessionalId = professionalId;
        Attributes = NormalizeAttributes(attributes);
    }

    public Guid TenantId { get; }
    public Guid ServiceId { get; }
    public Guid? ProfessionalId { get; }
    public IReadOnlyDictionary<string, string> Attributes { get; }

    private static IReadOnlyDictionary<string, string> NormalizeAttributes(IReadOnlyDictionary<string, string> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in attributes)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Duration attributes must have non-empty keys and values.", nameof(attributes));
            normalized[key.Trim()] = value.Trim();
        }
        return normalized;
    }
}

/// <summary>Resolved duration and the rule version that produced it.</summary>
public sealed record ServiceDurationEstimate
{
    public ServiceDurationEstimate(int durationMinutes, int minimumDurationMinutes, int maximumDurationMinutes, string ruleVersion, string reason)
    {
        if (durationMinutes <= 0 || durationMinutes > 1440) throw new ArgumentOutOfRangeException(nameof(durationMinutes));
        if (minimumDurationMinutes <= 0 || minimumDurationMinutes > maximumDurationMinutes) throw new ArgumentException("Duration limits are invalid.", nameof(minimumDurationMinutes));
        if (durationMinutes < minimumDurationMinutes || durationMinutes > maximumDurationMinutes) throw new ArgumentException("Duration must be within the configured limits.", nameof(durationMinutes));
        if (string.IsNullOrWhiteSpace(ruleVersion)) throw new ArgumentException("Rule version is required.", nameof(ruleVersion));
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason is required.", nameof(reason));
        DurationMinutes = durationMinutes;
        MinimumDurationMinutes = minimumDurationMinutes;
        MaximumDurationMinutes = maximumDurationMinutes;
        RuleVersion = ruleVersion.Trim();
        Reason = reason.Trim();
    }

    public int DurationMinutes { get; }
    public int MinimumDurationMinutes { get; }
    public int MaximumDurationMinutes { get; }
    public string RuleVersion { get; }
    public string Reason { get; }
}

public interface IServiceDurationRule
{
    string Version { get; }
    bool CanResolve(ServiceDurationContext context);
    ServiceDurationEstimate Resolve(ServiceDurationContext context, int baseDurationMinutes);
}
