using Shine.Domain;

namespace Shine.Infrastructure;

public interface ITenantExecutionContext
{
    Guid TenantId { get; }
    Guid? EffectiveTenantId { get; }
    bool IsBypass { get; }
    IDisposable EnterTenant(Guid tenantId);
    IDisposable EnterBypass();
}

public sealed class TenantExecutionContext(ICurrentTenant currentTenant) : ITenantExecutionContext
{
    private Guid? selectedTenantId;
    public Guid TenantId => EffectiveTenantId ?? throw new TenantIsolationException("An active tenant is required.");
    public Guid? EffectiveTenantId => selectedTenantId ?? currentTenant.TenantId;
    public bool IsBypass { get; private set; }

    public IDisposable EnterTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty) throw new TenantIsolationException("A valid tenant is required.");
        if (currentTenant.TenantId is Guid authenticatedTenantId && authenticatedTenantId != tenantId)
            throw new TenantIsolationException("An authenticated session cannot switch tenants.");
        var previous = selectedTenantId;
        selectedTenantId = tenantId;
        return new TenantScope(this, previous);
    }

    public IDisposable EnterBypass()
    {
        if (!currentTenant.Roles.Any(role =>
                role.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                role.Equals("system", StringComparison.OrdinalIgnoreCase)))
            throw new TenantIsolationException("Only an administrator or system process can bypass tenant isolation.");

        var previous = IsBypass;
        IsBypass = true;
        return new BypassScope(this, previous);
    }

    private sealed class BypassScope(TenantExecutionContext owner, bool previous) : IDisposable
    {
        public void Dispose() => owner.IsBypass = previous;
    }

    private sealed class TenantScope(TenantExecutionContext owner, Guid? previous) : IDisposable
    {
        public void Dispose() => owner.selectedTenantId = previous;
    }
}

public static class TenantIsolationExtensions
{
    public static IQueryable<T> ForTenant<T>(this IQueryable<T> query, Guid tenantId) where T : IMultiTenantEntity =>
        query.Where(entity => entity.TenantId == tenantId);

    public static void EnsureTenant<T>(this T entity, Guid tenantId) where T : IMultiTenantEntity
    {
        if (entity.TenantId != tenantId) throw new TenantIsolationException("The entity belongs to another tenant.");
    }
}
