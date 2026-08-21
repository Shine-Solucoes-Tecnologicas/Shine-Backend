using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Api.Controllers;
using Shine.Application;
using Shine.Domain.Identity;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class AdministrativeApiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Administrative_tenant_list_and_detail_return_existing_tenant()
    {
        await using var db = fixture.CreateDb();
        var tenant = new Tenant($"Administrative API {Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var controller = new AdministrativeTenantsController(db, new FakeCurrentUser());
        var list = await controller.List(new PagedRequest(PageSize: 100), tenant.Name, null, CancellationToken.None);
        var listResponse = Assert.IsType<OkObjectResult>(list.Result);
        var page = Assert.IsType<PagedResponse<AdministrativeTenantResponse>>(listResponse.Value);
        Assert.Contains(page.Items, item => item.Id == tenant.Id);

        var detail = await controller.Detail(tenant.Id, CancellationToken.None);
        var detailResponse = Assert.IsType<OkObjectResult>(detail.Result);
        var item = Assert.IsType<AdministrativeTenantResponse>(detailResponse.Value);
        Assert.Equal(tenant.Id, item.Id);
        Assert.Equal(tenant.Name, item.Name);
    }

    [Fact]
    public async Task Suspending_one_tenant_revokes_only_sessions_bound_to_that_tenant()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"multi-session-{Guid.NewGuid():N}@example.test", "hash");
        var suspendedTenant = new Tenant($"Suspended {Guid.NewGuid():N}");
        var activeTenant = new Tenant($"Active {Guid.NewGuid():N}");
        var suspendedMembership = new UserTenant(user.Id, suspendedTenant.Id, user.Id, false);
        var activeMembership = new UserTenant(user.Id, activeTenant.Id, user.Id, false);
        var suspendedSession = new RefreshToken(user.Id, suspendedTenant.Id, suspendedMembership.UserTenantId,
            $"suspended-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(1));
        var activeSession = new RefreshToken(user.Id, activeTenant.Id, activeMembership.UserTenantId,
            $"active-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(1));
        var globalSession = new RefreshToken(user.Id, null, null,
            $"global-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(1));
        db.AddRange(user, suspendedTenant, activeTenant, suspendedMembership, activeMembership,
            suspendedSession, activeSession, globalSession);
        await db.SaveChangesAsync();

        var response = await new AdministrativeTenantsController(db, new FakeCurrentUser())
            .Suspend(suspendedTenant.Id, new SuspendTenantRequest("Security test"), default);

        Assert.IsType<NoContentResult>(response);
        Assert.NotNull(suspendedSession.RevokedAtUtc);
        Assert.Null(activeSession.RevokedAtUtc);
        Assert.Null(globalSession.RevokedAtUtc);
        Assert.False(suspendedTenant.IsActive);
        Assert.True(activeTenant.IsActive);
    }

    private sealed class FakeCurrentUser : Shine.Infrastructure.ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public bool IsAuthenticated => true;
    }
}
