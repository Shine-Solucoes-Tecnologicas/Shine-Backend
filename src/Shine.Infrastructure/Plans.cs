using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IPlanAccess
{
    Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default);
    Task SetPlanAsync(Guid tenantId, Guid planId, CancellationToken cancellationToken = default);
    Task SetOverrideAsync(Guid tenantId, string moduleCode, bool enabled, CancellationToken cancellationToken = default);
}

public sealed class PlanAccess(ShineDbContext db) : IPlanAccess
{
    public async Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default)
    {
        var code = new ModuleCode(moduleCode).Value;
        var overrideValue = await db.TenantModuleOverrides.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ModuleCode == code, cancellationToken);
        if (overrideValue is not null) return overrideValue.Enabled;
        return await db.TenantPlans.Where(x => x.TenantId == tenantId).Join(db.PlanModules, x => x.PlanId, x => x.PlanId, (_, module) => module.ModuleCode).AnyAsync(x => x == code, cancellationToken);
    }

    public async Task SetPlanAsync(Guid tenantId, Guid planId, CancellationToken cancellationToken = default)
    {
        if (!await db.Plans.AnyAsync(x => x.Id == planId, cancellationToken)) throw new KeyNotFoundException("Plan not found.");
        var current = await db.TenantPlans.SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (current is null) db.TenantPlans.Add(new TenantPlan(tenantId, planId));
        else { db.TenantPlans.Remove(current); db.TenantPlans.Add(new TenantPlan(tenantId, planId)); }
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
