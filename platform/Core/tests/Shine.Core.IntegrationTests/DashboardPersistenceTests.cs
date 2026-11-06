using Shine.Domain;
using Shine.Infrastructure;
using Microsoft.EntityFrameworkCore;

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
        Assert.Equal("user", resolved.Source);
        Assert.Equal(userId, resolved.UserId);
        Assert.Equal(3, Assert.Single(resolved.Placements).PositionX);

        var otherUser = await service.GetResolvedAsync(tenantId, Guid.NewGuid(), dashboardKey);
        Assert.NotNull(otherUser);
        Assert.Equal("tenant", otherUser.Source);
        Assert.Null(otherUser.UserId);
        Assert.Equal(0, Assert.Single(otherUser.Placements).PositionX);
    }

    [Fact]
    public async Task Missing_persistence_returns_deterministic_system_layout_without_materializing_it()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var descriptor = new DashboardWidgetDescriptor("core.welcome", "CORE", "Welcome", "Welcome", "dashboard.read", 3, 2);
        await using var db = fixture.CreateDb(new TenantContext(tenantId));
        var service = CreateService(db, descriptor);

        var first = await service.GetResolvedAsync(tenantId, userId, "main");
        var second = await service.GetResolvedAsync(tenantId, userId, "main");

        Assert.Equal("system", first.Source);
        Assert.Null(first.Id);
        Assert.Equal(0, first.Version);
        Assert.Equal(first.ManifestVersion, second.ManifestVersion);
        Assert.Equal(first.Placements, second.Placements);
        Assert.Equal(3, Assert.Single(first.Placements).Width);
        Assert.Equal(2, Assert.Single(first.Placements).Height);
        Assert.False(await db.DashboardLayouts.AnyAsync(x => x.TenantId == tenantId));
    }

    [Fact]
    public async Task Reset_removes_only_the_user_override_and_is_idempotent_and_recreatable()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dashboardKey = $"TEST-{Guid.NewGuid():N}";
        var descriptor = new DashboardWidgetDescriptor("core.welcome", "CORE", "Welcome", "Welcome", "dashboard.read");
        int userVersion;
        await using (var seedDb = fixture.CreateDb())
        {
            var tenantDefault = new DashboardLayout(tenantId, null, dashboardKey);
            tenantDefault.ReplacePlacements([Placement(descriptor, 0)]);
            var userOverride = new DashboardLayout(tenantId, userId, dashboardKey);
            userOverride.ReplacePlacements([Placement(descriptor, 4)]);
            var otherTenantOverride = new DashboardLayout(otherTenantId, userId, dashboardKey);
            otherTenantOverride.ReplacePlacements([Placement(descriptor, 8)]);
            userVersion = userOverride.Version;
            seedDb.AddRange(tenantDefault, userOverride, otherTenantOverride);
            await seedDb.SaveChangesAsync();
        }

        await using var db = fixture.CreateDb(new TenantContext(tenantId));
        var service = CreateService(db, descriptor);
        var reset = await service.ResetAsync(tenantId, userId, new DashboardLayoutResetRequest(dashboardKey, userVersion));
        var repeated = await service.ResetAsync(tenantId, userId, new DashboardLayoutResetRequest(dashboardKey, 0));

        Assert.Equal("tenant", reset.Source);
        Assert.Equal("tenant", repeated.Source);
        Assert.Equal(0, reset.Version);
        Assert.Equal(0, Assert.Single(reset.Placements).PositionX);
        Assert.False(await db.DashboardLayouts.AnyAsync(x => x.TenantId == tenantId && x.UserId == userId));
        Assert.True(await db.DashboardLayouts.IgnoreQueryFilters().AnyAsync(x => x.TenantId == otherTenantId && x.UserId == userId && !x.IsDeleted));

        var recreated = await service.SaveAsync(tenantId, userId,
            new DashboardLayoutRequest(dashboardKey, 0, [Placement(descriptor, 6)]));
        Assert.Equal("user", recreated.Source);
        Assert.Equal(6, Assert.Single(recreated.Placements).PositionX);
    }

    [Fact]
    public async Task Save_requires_current_version_and_does_not_overwrite_a_newer_layout()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dashboardKey = $"TEST-{Guid.NewGuid():N}";
        var descriptor = new DashboardWidgetDescriptor("core.welcome", "CORE", "Welcome", "Welcome", "dashboard.read");

        DashboardLayoutResult created;
        await using (var createDb = fixture.CreateDb(new TenantContext(tenantId)))
            created = await CreateService(createDb, descriptor).SaveAsync(tenantId, userId,
                new DashboardLayoutRequest(dashboardKey, 0, [Placement(descriptor, 0)]));

        DashboardLayoutResult updated;
        await using (var updateDb = fixture.CreateDb(new TenantContext(tenantId)))
            updated = await CreateService(updateDb, descriptor).SaveAsync(tenantId, userId,
                new DashboardLayoutRequest(dashboardKey, created.Version, [Placement(descriptor, 3)]));

        await using var staleDb = fixture.CreateDb(new TenantContext(tenantId));
        var service = CreateService(staleDb, descriptor);
        var exception = await Assert.ThrowsAsync<StaleDashboardLayoutException>(() => service.SaveAsync(
            tenantId, userId, new DashboardLayoutRequest(dashboardKey, created.Version, [Placement(descriptor, 6)])));

        Assert.Equal(updated.Version, exception.CurrentVersion);
        var persisted = await service.GetResolvedAsync(tenantId, userId, dashboardKey);
        Assert.Equal(3, Assert.Single(persisted!.Placements).PositionX);
    }

    [Fact]
    public async Task Concurrent_updates_from_the_same_version_have_exactly_one_winner()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dashboardKey = $"TEST-{Guid.NewGuid():N}";
        var descriptor = new DashboardWidgetDescriptor("core.welcome", "CORE", "Welcome", "Welcome", "dashboard.read");

        await using (var seedDb = fixture.CreateDb(new TenantContext(tenantId)))
        {
            var layout = new DashboardLayout(tenantId, userId, dashboardKey);
            layout.ReplacePlacements([Placement(descriptor, 0)]);
            seedDb.DashboardLayouts.Add(layout);
            await seedDb.SaveChangesAsync();
        }

        await using var firstDb = fixture.CreateDb(new TenantContext(tenantId));
        await using var secondDb = fixture.CreateDb(new TenantContext(tenantId));
        var normalizedKey = dashboardKey.ToUpperInvariant();
        var first = await firstDb.DashboardLayouts.Include(x => x.Placements).SingleAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == normalizedKey);
        var second = await secondDb.DashboardLayouts.Include(x => x.Placements).SingleAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == normalizedKey);
        first.ReplacePlacements([Placement(descriptor, 2)]);
        second.ReplacePlacements([Placement(descriptor, 4)]);

        var outcomes = await Task.WhenAll(Capture(firstDb.SaveChangesAsync()), Capture(secondDb.SaveChangesAsync()));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is DbUpdateConcurrencyException);
    }

    [Fact]
    public async Task Concurrent_reset_and_edit_from_the_same_version_have_exactly_one_winner()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var dashboardKey = $"TEST-{Guid.NewGuid():N}";
        var descriptor = new DashboardWidgetDescriptor("core.welcome", "CORE", "Welcome", "Welcome", "dashboard.read");
        await using (var seedDb = fixture.CreateDb(new TenantContext(tenantId)))
        {
            var layout = new DashboardLayout(tenantId, userId, dashboardKey);
            layout.ReplacePlacements([Placement(descriptor, 0)]);
            seedDb.Add(layout);
            await seedDb.SaveChangesAsync();
        }

        await using var resetDb = fixture.CreateDb(new TenantContext(tenantId));
        await using var editDb = fixture.CreateDb(new TenantContext(tenantId));
        var normalizedKey = dashboardKey.ToUpperInvariant();
        var reset = await resetDb.DashboardLayouts.SingleAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == normalizedKey);
        var edit = await editDb.DashboardLayouts.Include(x => x.Placements).SingleAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == normalizedKey);
        reset.AdvanceVersion();
        reset.Delete(DateTime.UtcNow);
        edit.ReplacePlacements([Placement(descriptor, 5)]);

        var outcomes = await Task.WhenAll(Capture(resetDb.SaveChangesAsync()), Capture(editDb.SaveChangesAsync()));

        Assert.Single(outcomes, outcome => outcome is null);
        Assert.Single(outcomes, outcome => outcome is DbUpdateConcurrencyException);
    }

    private static DashboardLayoutService CreateService(Shine.Infrastructure.Persistence.ShineDbContext db, DashboardWidgetDescriptor descriptor)
    {
        var catalog = new DashboardWidgetCatalog();
        catalog.Register(new Provider(descriptor));
        return new DashboardLayoutService(db, catalog, new Resolver(descriptor));
    }

    private static DashboardWidgetPlacement Placement(DashboardWidgetDescriptor descriptor, int positionX) =>
        new(descriptor.WidgetKey, descriptor.ModuleKey, positionX, 0, 2, 1);

    private static async Task<Exception?> Capture(Task operation)
    {
        try { await operation; return null; }
        catch (Exception exception) { return exception; }
    }

    private sealed class TenantContext(Guid tenantId) : ICurrentTenant
    {
        public Guid? TenantId { get; } = tenantId;
        public Guid? UserTenantId => TenantId;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
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
