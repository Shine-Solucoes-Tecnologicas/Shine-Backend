using BusinessCatalog.Domain;
using Microsoft.AspNetCore.Mvc;
using Scheduling.Domain;
using Shine.Api;
using Shine.Api.Controllers;
using Shine.Domain.Authorization;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class EffectiveProfessionalCapacityEndpointTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Returns_tenant_default_and_current_grid_when_professional_has_no_override()
    {
        var tenant = new TestTenant(Guid.NewGuid());
        var professional = await AddProfessionalAsync(tenant);
        await using var db = fixture.CreateSchedulingDb(tenant);
        var settings = new SchedulingSettings(tenant.Id);
        settings.Update(20, 0, 0, "UTC", defaultMaxConcurrentAppointments: 4);
        db.SchedulingSettings.Add(settings);
        await db.SaveChangesAsync();
        var controller = CreateController(db, tenant);

        var result = await controller.GetProfessionalCapacity(professional.Id, default);

        var response = Assert.IsType<EffectiveProfessionalCapacityResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(professional.Id, response.ProfessionalId);
        Assert.Equal(4, response.EffectiveCapacity);
        Assert.Equal(4, response.DefaultCapacity);
        Assert.Equal("default", response.Source);
        Assert.Equal(20, response.GridIntervalMinutes);
        Assert.True(response.PartialOccupancyConsumesWholeBucket);
    }

    [Fact]
    public async Task Returns_override_without_hiding_the_current_default()
    {
        var tenant = new TestTenant(Guid.NewGuid());
        var professional = await AddProfessionalAsync(tenant);
        await using var db = fixture.CreateSchedulingDb(tenant);
        var settings = new SchedulingSettings(tenant.Id);
        settings.Update(10, 0, 0, "UTC", defaultMaxConcurrentAppointments: 5);
        db.AddRange(settings, new ProfessionalSchedulingSettings(tenant.Id, professional.Id, 2));
        await db.SaveChangesAsync();
        var controller = CreateController(db, tenant);

        var result = await controller.GetProfessionalCapacity(professional.Id, default);

        var response = Assert.IsType<EffectiveProfessionalCapacityResponse>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Equal(2, response.EffectiveCapacity);
        Assert.Equal(5, response.DefaultCapacity);
        Assert.Equal("override", response.Source);
        Assert.Equal(10, response.GridIntervalMinutes);
    }

    [Fact]
    public async Task Does_not_reveal_inactive_cross_tenant_or_out_of_scope_professionals()
    {
        var tenant = new TestTenant(Guid.NewGuid());
        var inactive = await AddProfessionalAsync(tenant, active: false);
        var foreign = await AddProfessionalAsync(new TestTenant(Guid.NewGuid()));
        await using var db = fixture.CreateSchedulingDb(tenant);

        var controller = CreateController(db, tenant);
        Assert.IsType<NotFoundResult>((await controller.GetProfessionalCapacity(inactive.Id, default)).Result);
        Assert.IsType<NotFoundResult>((await controller.GetProfessionalCapacity(foreign.Id, default)).Result);

        var hiddenController = CreateController(db, tenant,
            new FixedScopeAuthorization(new(SchedulingScopeDecisionStatus.Hidden, PermissionScope.Own)));
        Assert.IsType<NotFoundResult>((await hiddenController.GetProfessionalCapacity(Guid.NewGuid(), default)).Result);
    }

    private async Task<Professional> AddProfessionalAsync(TestTenant tenant, bool active = true)
    {
        var professional = new Professional(tenant.Id, $"Professional {Guid.NewGuid():N}");
        if (!active) professional.SetActive(false);
        await using var catalog = fixture.CreateBusinessCatalogDb();
        catalog.Professionals.Add(professional);
        await catalog.SaveChangesAsync();
        return professional;
    }

    private SchedulingController CreateController(
        Scheduling.Infrastructure.SchedulingDbContext db,
        TestTenant tenant,
        ISchedulingScopeAuthorization? authorization = null) =>
        new(db, tenant, null!, null!, null!, null!, authorization ?? new FixedScopeAuthorization(
            new(SchedulingScopeDecisionStatus.Allowed, PermissionScope.All)),
            businessCatalog: fixture.CreateBusinessCatalogReader(tenant));

    private sealed record TestTenant(Guid Id) : ICurrentTenant
    {
        public Guid? TenantId => Id;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }

    private sealed class FixedScopeAuthorization(SchedulingScopeDecision decision) : ISchedulingScopeAuthorization
    {
        public Task<SchedulingScopeDecision> AuthorizeAsync(string permissionCode, Guid? requestedProfessionalId = null,
            CancellationToken cancellationToken = default) => Task.FromResult(
                decision.Allowed ? decision with { ProfessionalId = requestedProfessionalId } : decision);

        public Task<SchedulingScopeDecision> AuthorizeAllAsync(string permissionCode,
            CancellationToken cancellationToken = default) => Task.FromResult(decision);
    }
}
