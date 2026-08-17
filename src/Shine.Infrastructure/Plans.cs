using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IPlanAccess
{
    Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default);
    Task<IReadOnlySet<string>> GetAccessibleModuleCodesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, long>> GetEntitlementValuesAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, EntitlementGrant>> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task SetPlanAsync(Guid tenantId, Guid planId, CancellationToken cancellationToken = default);
    Task RemovePlanAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task SetOverrideAsync(Guid tenantId, string moduleCode, bool enabled, CancellationToken cancellationToken = default);
}

public sealed class PlanAccess(ShineDbContext db) : IPlanAccess
{
    public async Task<IReadOnlyDictionary<string, long>> GetEntitlementValuesAsync(Guid tenantId, CancellationToken cancellationToken = default)
        => (await GetEntitlementsAsync(tenantId, cancellationToken)).Where(x => x.Value.IsEffective(DateTime.UtcNow)).ToDictionary(x => x.Key, x => x.Value.Value);

    public async Task<IReadOnlyDictionary<string, EntitlementGrant>> GetEntitlementsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var values = await db.TenantPlans.AsNoTracking().Where(x => x.TenantId == tenantId)
            .Join(db.PlanEntitlements, x => x.PlanId, x => x.PlanId, (_, item) => item)
            .ToDictionaryAsync(x => x.Key, x => new EntitlementGrant(x.Key, x.Value, "plan", x.State, x.StartsAtUtc, x.ExpiresAtUtc, x.Version, x.IsUnlimited), cancellationToken);
        var overrides = await db.TenantEntitlementOverrides.AsNoTracking().Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.Key, x => new EntitlementGrant(x.Key, x.Value, "tenant-override", x.State, x.StartsAtUtc, x.ExpiresAtUtc, x.Version, x.IsUnlimited), cancellationToken);
        foreach (var (key, value) in overrides) values[key] = value;
        return values;
    }

    public async Task<IReadOnlySet<string>> GetAccessibleModuleCodesAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var planModules = await db.TenantPlans.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .Join(db.PlanModules, x => x.PlanId, x => x.PlanId, (_, module) => module.ModuleCode)
            .ToHashSetAsync(cancellationToken);
        var overrides = await db.TenantModuleOverrides.AsNoTracking()
            .Where(x => x.TenantId == tenantId)
            .ToDictionaryAsync(x => x.ModuleCode, x => x.Enabled, cancellationToken);
        foreach (var (module, enabled) in overrides)
            if (enabled) planModules.Add(module); else planModules.Remove(module);
        // CORE is the platform baseline and is not a commercial module.
        planModules.Add("CORE");
        return planModules;
    }

    public async Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default)
    {
        var code = new ModuleCode(moduleCode).Value;
        return (await GetAccessibleModuleCodesAsync(tenantId, cancellationToken)).Contains(code);
    }

    public async Task SetPlanAsync(Guid tenantId, Guid planId, CancellationToken cancellationToken = default)
    {
        if (!await db.Plans.AnyAsync(x => x.Id == planId, cancellationToken)) throw new KeyNotFoundException("Plan not found.");
        var current = await db.TenantPlans.SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (current is null) db.TenantPlans.Add(new TenantPlan(tenantId, planId));
        else if (current.PlanId != planId) current.ChangePlan(planId);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemovePlanAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var current = await db.TenantPlans.SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (current is null) return;
        db.TenantPlans.Remove(current);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetOverrideAsync(Guid tenantId, string moduleCode, bool enabled, CancellationToken cancellationToken = default)
    {
        var code = new ModuleCode(moduleCode).Value;
        var current = await db.TenantModuleOverrides.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ModuleCode == code, cancellationToken);
        if (current is null) db.TenantModuleOverrides.Add(new TenantModuleOverride(tenantId, code, enabled)); else current.SetEnabled(enabled);
        await db.SaveChangesAsync(cancellationToken);
    }
}
