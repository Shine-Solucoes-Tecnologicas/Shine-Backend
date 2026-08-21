using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class DashboardResolutionTests
{
    [Fact]
    public async Task Resolver_returns_only_widgets_from_enabled_modules_and_allowed_permissions()
    {
        var catalog = new DashboardWidgetCatalog();
        catalog.Register(new Provider("agenda.next", "SCHEDULING", "scheduling.read"));
        catalog.Register(new Provider("admin.summary", "ADMIN", "admin.read"));
        var resolver = new DashboardWidgetResolver(catalog, new ModuleAccessStub("SCHEDULING"), new PermissionStub("scheduling.read"));

        var result = await resolver.ResolveAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Single(result);
        Assert.Equal("AGENDA.NEXT", result.Single().Descriptor.WidgetKey);
    }

    [Fact]
    public async Task Resolver_returns_no_widgets_without_user_context()
    {
        var catalog = new DashboardWidgetCatalog();
        catalog.Register(new Provider("agenda.next", "SCHEDULING", "scheduling.read"));
        var resolver = new DashboardWidgetResolver(catalog, new ModuleAccessStub("SCHEDULING"), new PermissionStub("scheduling.read"));

        var result = await resolver.ResolveAsync(Guid.NewGuid(), null);

        Assert.Empty(result);
    }

    private sealed class Provider(string key, string module, string permission) : IDashboardWidgetProvider
    {
        public DashboardWidgetDescriptor Descriptor { get; } = new(key, module, key, key, permission);
        public Task<DashboardWidgetData> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken = default) => Task.FromResult(new DashboardWidgetData(Descriptor.WidgetKey, new Dictionary<string, object?>()));
    }

    private sealed class ModuleAccessStub(params string[] enabled) : IModuleAccess
    {
        public Task<IReadOnlyCollection<ModuleDescriptor>> GetAccessibleAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ModuleDescriptor>>(enabled.Select(code => ModuleDescriptor.Create(code, code, code)).ToArray());
        public Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default) => Task.FromResult(enabled.Contains(new ModuleCode(moduleCode).Value));
        public Task SetAsync(Guid tenantId, string moduleCode, bool enabled, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PermissionStub(params string[] allowed) : IPermissionAuthorization
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(allowed.Contains(permissionCode));
    }
}
