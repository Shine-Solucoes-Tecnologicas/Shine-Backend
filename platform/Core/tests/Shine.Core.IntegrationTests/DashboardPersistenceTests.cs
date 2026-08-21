using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class DashboardPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task User_layout_takes_precedence_over_tenant_default()
    {
        await using var db = fixture.CreateDb();
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dashboardKey = $"TEST-{Guid.NewGuid():N}";
        var catalog = new DashboardWidgetCatalog();
        var descriptor = new DashboardWidgetDescriptor("core.welcome", "CORE", "Welcome", "Welcome", "dashboard.read");
        catalog.Register(new Provider(descriptor));
        var resolver = new Resolver(descriptor);
        var service = new DashboardLayoutService(db, catalog, resolver);

        var tenantLayout = new DashboardLayout(tenantId, null, dashboardKey);
        tenantLayout.ReplacePlacements([new DashboardWidgetPlacement(descriptor.WidgetKey, descriptor.ModuleKey, 0, 0, 2, 1)]);
        var userLayout = new DashboardLayout(tenantId, userId, dashboardKey);
        userLayout.ReplacePlacements([new DashboardWidgetPlacement(descriptor.WidgetKey, descriptor.ModuleKey, 3, 0, 2, 1)]);
        db.DashboardLayouts.AddRange(tenantLayout, userLayout);
        await db.SaveChangesAsync();

        var resolved = await service.GetResolvedAsync(tenantId, userId, dashboardKey);
        Assert.NotNull(resolved);
        Assert.Equal(userId, resolved.UserId);
        Assert.Equal(3, Assert.Single(resolved.Placements).PositionX);
    }

    private sealed class Provider(DashboardWidgetDescriptor descriptor) : IDashboardWidgetProvider
    {
        public DashboardWidgetDescriptor Descriptor { get; } = descriptor;
        public Task<DashboardWidgetData> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardWidgetData(Descriptor.WidgetKey, new Dictionary<string, object?>()));
    }

    private sealed class Resolver(DashboardWidgetDescriptor descriptor) : IDashboardWidgetResolver
    {
        public Task<IReadOnlyCollection<AvailableDashboardWidget>> ResolveAsync(Guid tenantId, Guid? userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyCollection<AvailableDashboardWidget>>([new AvailableDashboardWidget(descriptor)]);
    }
}
