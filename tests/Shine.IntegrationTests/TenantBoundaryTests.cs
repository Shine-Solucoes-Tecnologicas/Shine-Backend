using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class TenantBoundaryTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Tenant_filter_hides_notifications_from_other_tenants()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        fixture.Db.Notifications.Add(Notification.Create(tenantA, null, "test", "A", "tenant A"));
        fixture.Db.Notifications.Add(Notification.Create(tenantB, null, "test", "B", "tenant B"));
        await fixture.Db.SaveChangesAsync();

        var visible = await fixture.Db.Notifications
            .IgnoreQueryFilters()
            .Where(notification => notification.TenantId == tenantA)
            .ToArrayAsync();

        Assert.Single(visible);
        Assert.Equal("A", visible[0].Title);
    }

    [Fact]
    public async Task Tenant_context_query_filter_hides_other_tenants_without_manual_filter()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        await using (var seedDb = fixture.CreateDb())
        {
            seedDb.Notifications.Add(Notification.Create(tenantA, null, "test", "A", "tenant A"));
            seedDb.Notifications.Add(Notification.Create(tenantB, null, "test", "B", "tenant B"));
            await seedDb.SaveChangesAsync();
        }

        await using var tenantDb = fixture.CreateDb(new FakeTenant(tenantA));
        var visible = await tenantDb.Notifications
            .Where(notification => notification.Title == "A" || notification.Title == "B")
            .ToArrayAsync();

        Assert.Single(visible);
        Assert.Equal("A", visible[0].Title);
    }

    private sealed class FakeTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }
}
