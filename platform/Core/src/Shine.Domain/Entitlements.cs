namespace Shine.Domain;

public enum EntitlementState { Active, Suspended, Revoked }
public enum EntitlementLimitStatus { NotConfigured, Unavailable, Available, Unlimited, Exhausted }

public sealed record EntitlementLimitDecision(
    string Key,
    EntitlementLimitStatus Status,
    long CurrentUsage,
    long Requested,
    long? Limit,
    long? Remaining)
{
    public bool Allowed => Status is EntitlementLimitStatus.Available or EntitlementLimitStatus.Unlimited;
}

public sealed class EntitlementUsage : AuditableEntity, IMultiTenantEntity
{
    private EntitlementUsage() { }
    public EntitlementUsage(Guid unitId, string key)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("Unit is required.", nameof(unitId));
        Id = Guid.NewGuid(); TenantId = unitId; Key = PlanEntitlement.NormalizeKey(key);
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public long Used { get; private set; }
    public long Version { get; private set; }

    public void Reserve(long quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        Used = checked(Used + quantity); Version++;
    }

    public void Release(long quantity)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        Used = Math.Max(0, Used - quantity); Version++;
    }

    public void Reconcile(long authoritativeUsage)
    {
        if (authoritativeUsage < 0) throw new ArgumentOutOfRangeException(nameof(authoritativeUsage));
        if (Used == authoritativeUsage) return;
        Used = authoritativeUsage;
        Version++;
    }
}

public enum EntitlementReservationStatus { Reserved, Released }

public sealed class EntitlementReservation : AuditableEntity, IMultiTenantEntity
{
    private EntitlementReservation() { }
    public EntitlementReservation(Guid tenantId, string key, Guid operationId, long quantity)
    {
        if (tenantId == Guid.Empty || operationId == Guid.Empty) throw new ArgumentException("Tenant and operation are required.");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
        TenantId = tenantId; Key = PlanEntitlement.NormalizeKey(key); OperationId = operationId; Quantity = quantity;
        Status = EntitlementReservationStatus.Reserved;
    }
    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public Guid OperationId { get; private set; }
    public long Quantity { get; private set; }
    public EntitlementReservationStatus Status { get; private set; }
    public DateTime? ReleasedAtUtc { get; private set; }
    public void Release(DateTime utcNow) { if (Status == EntitlementReservationStatus.Released) return; Status = EntitlementReservationStatus.Released; ReleasedAtUtc = utcNow; }
    public void ReserveAgain() { Status = EntitlementReservationStatus.Reserved; ReleasedAtUtc = null; }
}

public sealed record EntitlementGrant
{
    public EntitlementGrant(string key, long value, string origin, EntitlementState state = EntitlementState.Active,
        DateTime? startsAtUtc = null, DateTime? expiresAtUtc = null, int version = 1, bool isUnlimited = false)
    {
        Key = PlanEntitlement.NormalizeKey(key);
        if (value < 0) throw new ArgumentOutOfRangeException(nameof(value));
        if (string.IsNullOrWhiteSpace(origin)) throw new ArgumentException("Origin is required.", nameof(origin));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (startsAtUtc is { Kind: not DateTimeKind.Utc } || expiresAtUtc is { Kind: not DateTimeKind.Utc }) throw new ArgumentException("Entitlement validity must use UTC.");
        if (startsAtUtc is not null && expiresAtUtc is not null && startsAtUtc >= expiresAtUtc) throw new ArgumentException("Entitlement validity is invalid.");
        Value = value; Origin = origin.Trim(); State = state; StartsAtUtc = startsAtUtc; ExpiresAtUtc = expiresAtUtc; Version = version; IsUnlimited = isUnlimited;
    }

    public string Key { get; }
    public long Value { get; }
    public string Origin { get; }
    public EntitlementState State { get; }
    public DateTime? StartsAtUtc { get; }
    public DateTime? ExpiresAtUtc { get; }
    public int Version { get; }
    public bool IsUnlimited { get; }
    public bool IsEffective(DateTime utcNow) => State == EntitlementState.Active &&
        (StartsAtUtc is null || StartsAtUtc <= utcNow) && (ExpiresAtUtc is null || ExpiresAtUtc > utcNow);
}

public sealed record EntitlementSnapshot(
    Guid UnitId,
    IReadOnlySet<string> Modules,
    IReadOnlyDictionary<string, EntitlementGrant> Entitlements)
{
    public EntitlementSnapshot(Guid unitId, IReadOnlySet<string> modules, IReadOnlyDictionary<string, long> limits)
        : this(unitId, modules, limits.ToDictionary(x => PlanEntitlement.NormalizeKey(x.Key), x => new EntitlementGrant(x.Key, x.Value, "legacy"))) { }

    public IReadOnlyDictionary<string, long> Limits => Entitlements.Where(x => x.Value.IsEffective(DateTime.UtcNow)).ToDictionary(x => x.Key, x => x.Value.Value);
    public bool HasModule(string moduleCode) => Modules.Contains(new ModuleCode(moduleCode).Value);

    public bool Allows(string limitCode, long requested = 1)
    {
        return EvaluateLimit(limitCode, 0, requested, DateTime.UtcNow).Allowed;
    }

    public EntitlementLimitDecision EvaluateLimit(string limitCode, long currentUsage, long requested = 1, DateTime? utcNow = null)
    {
        if (currentUsage < 0) throw new ArgumentOutOfRangeException(nameof(currentUsage));
        if (requested <= 0) throw new ArgumentOutOfRangeException(nameof(requested));
        var key = PlanEntitlement.NormalizeKey(limitCode);
        if (!Entitlements.TryGetValue(key, out var grant))
            return new(key, EntitlementLimitStatus.NotConfigured, currentUsage, requested, null, null);
        if (!grant.IsEffective(utcNow ?? DateTime.UtcNow))
            return new(key, EntitlementLimitStatus.Unavailable, currentUsage, requested, grant.IsUnlimited ? null : grant.Value, null);
        if (grant.IsUnlimited)
            return new(key, EntitlementLimitStatus.Unlimited, currentUsage, requested, null, null);

        var remaining = Math.Max(0, grant.Value - currentUsage);
        return requested <= remaining
            ? new(key, EntitlementLimitStatus.Available, currentUsage, requested, grant.Value, remaining - requested)
            : new(key, EntitlementLimitStatus.Exhausted, currentUsage, requested, grant.Value, remaining);
    }
}
