using Shine.Domain;

namespace Shine.Infrastructure;

public sealed record AvailableDashboardWidget(DashboardWidgetDescriptor Descriptor);

public interface IDashboardWidgetResolver
{
    Task<IReadOnlyCollection<AvailableDashboardWidget>> ResolveAsync(Guid tenantId, Guid? userId, CancellationToken cancellationToken = default);
}

public sealed class DashboardWidgetResolver(IDashboardWidgetCatalog catalog, IModuleAccess moduleAccess, IPermissionAuthorization permissions) : IDashboardWidgetResolver
{
    public async Task<IReadOnlyCollection<AvailableDashboardWidget>> ResolveAsync(Guid tenantId, Guid? userId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (userId is null) return [];

        var modules = await moduleAccess.GetAccessibleAsync(tenantId, cancellationToken);
        var available = new List<AvailableDashboardWidget>();
        foreach (var descriptor in catalog.Descriptors.Where(widget => modules.Any(module => module.Code.Value == widget.ModuleKey)))
        {
            if (await permissions.HasPermissionAsync(userId.Value, tenantId, descriptor.RequiredPermission, cancellationToken))
                available.Add(new AvailableDashboardWidget(descriptor));
        }
        return available;
    }
}
