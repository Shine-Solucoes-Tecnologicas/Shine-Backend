using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

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
}
