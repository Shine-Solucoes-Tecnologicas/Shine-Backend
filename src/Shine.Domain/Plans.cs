namespace Shine.Domain;

public sealed class Plan : AuditableEntity
{
    private Plan() { }
    public Plan(string code, string name)
    {
        Code = Normalize(code);
        Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("Plan name is required.", nameof(name)) : name.Trim();
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public ICollection<PlanModule> Modules { get; private set; } = new List<PlanModule>();
    public static string Normalize(string value) => new ModuleCode(value).Value;
}

public sealed class PlanModule
{
    private PlanModule() { }
    public PlanModule(Guid planId, string moduleCode) { PlanId = planId; ModuleCode = new ModuleCode(moduleCode).Value; }
    public Guid PlanId { get; private set; }
    public string ModuleCode { get; private set; } = null!;
}

public sealed class PlanEntitlement
{
    private PlanEntitlement() { }
    public PlanEntitlement(Guid planId, string key, long value, EntitlementState state = EntitlementState.Active, DateTime? startsAtUtc = null, DateTime? expiresAtUtc = null, int version = 1, bool isUnlimited = false)
    {
        PlanId = planId;
        var grant = new EntitlementGrant(key, value, "plan", state, startsAtUtc, expiresAtUtc, version, isUnlimited);
        Key = grant.Key; Value = grant.Value; State = grant.State; StartsAtUtc = grant.StartsAtUtc; ExpiresAtUtc = grant.ExpiresAtUtc; Version = grant.Version; IsUnlimited = grant.IsUnlimited;
    }
    public Guid PlanId { get; private set; }
    public string Key { get; private set; } = null!;
    public long Value { get; private set; }
    public EntitlementState State { get; private set; }
    public DateTime? StartsAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public int Version { get; private set; }
    public bool IsUnlimited { get; private set; }
    public void ChangeState(EntitlementState state) { State = state; Version++; }
    public static string NormalizeKey(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Entitlement key is required.", nameof(value)) : value.Trim().ToUpperInvariant();
}

public sealed class TenantPlan : AuditableEntity, IMultiTenantEntity
{
    private TenantPlan() { }
    public TenantPlan(Guid tenantId, Guid planId) { TenantId = tenantId; PlanId = planId; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public void ChangePlan(Guid planId)
    {
        if (planId == Guid.Empty) throw new ArgumentException("Plan is required.", nameof(planId));
        PlanId = planId;
    }
}

public sealed class TenantModuleOverride : AuditableEntity, IMultiTenantEntity
{
    private TenantModuleOverride() { }
    public TenantModuleOverride(Guid tenantId, string moduleCode, bool enabled) { TenantId = tenantId; ModuleCode = new ModuleCode(moduleCode).Value; Enabled = enabled; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string ModuleCode { get; private set; } = null!;
    public bool Enabled { get; private set; }
    public void SetEnabled(bool enabled) => Enabled = enabled;
}

public sealed class TenantEntitlementOverride : AuditableEntity, IMultiTenantEntity
{
    private TenantEntitlementOverride() { }
    public TenantEntitlementOverride(Guid tenantId, string key, long value, EntitlementState state = EntitlementState.Active, DateTime? startsAtUtc = null, DateTime? expiresAtUtc = null, int version = 1, bool isUnlimited = false) { TenantId = tenantId; var grant = new EntitlementGrant(key, value, "tenant-override", state, startsAtUtc, expiresAtUtc, version, isUnlimited); Key = grant.Key; Value = grant.Value; State = grant.State; StartsAtUtc = grant.StartsAtUtc; ExpiresAtUtc = grant.ExpiresAtUtc; Version = grant.Version; IsUnlimited = grant.IsUnlimited; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Key { get; private set; } = null!;
    public long Value { get; private set; }
    public EntitlementState State { get; private set; }
    public DateTime? StartsAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public int Version { get; private set; }
    public bool IsUnlimited { get; private set; }
    public void ChangeState(EntitlementState state) { State = state; Version++; }
}
