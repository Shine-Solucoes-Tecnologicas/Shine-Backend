using Shine.Domain;

namespace Shine.Infrastructure;

public interface ITenantExecutionContext
{
    Guid TenantId { get; }
    bool IsBypass { get; }
    IDisposable EnterBypass();
}

public sealed class TenantExecutionContext(ICurrentTenant currentTenant) : ITenantExecutionContext
{
    public Guid TenantId => currentTenant.TenantId ?? throw new TenantIsolationException("An active tenant is required.");
    public bool IsBypass { get; private set; }

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
