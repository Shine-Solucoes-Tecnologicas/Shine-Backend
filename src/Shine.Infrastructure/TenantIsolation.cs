using Shine.Domain;

namespace Shine.Infrastructure;

public interface ITenantExecutionContext
{
    Guid TenantId { get; }
    bool IsBypass { get; }
}

public sealed class TenantExecutionContext(ICurrentTenant currentTenant) : ITenantExecutionContext
{
    public Guid TenantId => currentTenant.TenantId ?? throw new TenantIsolationException("An active tenant is required.");
    public bool IsBypass => false;
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
