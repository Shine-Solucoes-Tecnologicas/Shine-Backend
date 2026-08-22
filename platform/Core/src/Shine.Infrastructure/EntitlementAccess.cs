using Shine.Domain;

namespace Shine.Infrastructure;

public interface IEntitlementAccess
{
    Task<EntitlementSnapshot> GetAsync(Guid unitId, CancellationToken cancellationToken = default);
    Task<bool> HasModuleAsync(Guid unitId, string moduleCode, CancellationToken cancellationToken = default);
    Task<bool> AllowsAsync(Guid unitId, string limitCode, long requested = 1, CancellationToken cancellationToken = default);
    Task<EntitlementGrant?> EvaluateAsync(Guid unitId, string key, CancellationToken cancellationToken = default);
    Task<EntitlementLimitDecision> EvaluateLimitAsync(Guid unitId, string key, long currentUsage, long requested = 1, CancellationToken cancellationToken = default);
}

public sealed class EntitlementAccess(IPlanAccess plans) : IEntitlementAccess
{
    public async Task<EntitlementSnapshot> GetAsync(Guid unitId, CancellationToken cancellationToken = default)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("Unit is required.", nameof(unitId));
        return new EntitlementSnapshot(unitId, await plans.GetAccessibleModuleCodesAsync(unitId, cancellationToken),
            await plans.GetEntitlementsAsync(unitId, cancellationToken));
    }

    public async Task<bool> HasModuleAsync(Guid unitId, string moduleCode, CancellationToken cancellationToken = default) =>
        (await GetAsync(unitId, cancellationToken)).HasModule(moduleCode);

    public async Task<bool> AllowsAsync(Guid unitId, string limitCode, long requested = 1, CancellationToken cancellationToken = default) =>
        (await GetAsync(unitId, cancellationToken)).Allows(limitCode, requested);

    public async Task<EntitlementGrant?> EvaluateAsync(Guid unitId, string key, CancellationToken cancellationToken = default)
    {
        var entitlements = await plans.GetEntitlementsAsync(unitId, cancellationToken);
        return entitlements.GetValueOrDefault(PlanEntitlement.NormalizeKey(key));
    }

    public async Task<EntitlementLimitDecision> EvaluateLimitAsync(Guid unitId, string key, long currentUsage, long requested = 1, CancellationToken cancellationToken = default) =>
        (await GetAsync(unitId, cancellationToken)).EvaluateLimit(key, currentUsage, requested);
}
