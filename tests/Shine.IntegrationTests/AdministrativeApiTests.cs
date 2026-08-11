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

    private sealed class FakeCurrentUser : Shine.Infrastructure.ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public bool IsAuthenticated => true;
    }
}
