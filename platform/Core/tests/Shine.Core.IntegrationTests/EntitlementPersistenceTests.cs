using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class EntitlementPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Tenant_override_has_precedence_and_other_tenant_is_isolated()
    {
        await using var db = fixture.CreateDb();
        var tenant = new Tenant($"Entitlement {Guid.NewGuid():N}");
        var otherTenant = new Tenant($"Entitlement {Guid.NewGuid():N}");
        var plan = new Plan($"PLAN-{Guid.NewGuid():N}", "Plan");
        db.AddRange(tenant, otherTenant, plan);
        db.PlanEntitlements.Add(new PlanEntitlement(plan.Id, "appointments.max", 10));
        db.TenantPlans.Add(new TenantPlan(tenant.Id, plan.Id));
        db.TenantEntitlementOverrides.Add(new TenantEntitlementOverride(tenant.Id, "appointments.max", 25));
        await db.SaveChangesAsync();

        var access = new PlanAccess(db);
        var grants = await access.GetEntitlementsAsync(tenant.Id);
        var grant = Assert.Single(grants).Value;
        Assert.Equal(25, grant.Value);
        Assert.Equal("tenant-override", grant.Origin);
        Assert.Empty(await access.GetEntitlementsAsync(otherTenant.Id));
    }

    [Fact]
    public async Task Unlimited_entitlement_is_persisted_and_evaluated_without_a_fixed_business_value()
    {
        await using var db = fixture.CreateDb();
        var tenant = new Tenant($"Unlimited {Guid.NewGuid():N}");
        var plan = new Plan($"PLAN-{Guid.NewGuid():N}", "Unlimited plan");
        db.AddRange(tenant, plan);
        db.PlanEntitlements.Add(new PlanEntitlement(plan.Id, "resources.max", 0, isUnlimited: true));
        db.TenantPlans.Add(new TenantPlan(tenant.Id, plan.Id));
        await db.SaveChangesAsync();

        var access = new EntitlementAccess(new PlanAccess(db));
        var decision = await access.EvaluateLimitAsync(tenant.Id, "resources.max", long.MaxValue - 1, 1);

        Assert.True(decision.Allowed);
        Assert.Equal(EntitlementLimitStatus.Unlimited, decision.Status);
        Assert.Null(decision.Limit);
    }

    [Fact]
    public async Task Concurrent_reservations_cannot_exceed_the_configured_limit()
    {
        Guid tenantId;
        await using (var setupDb = fixture.CreateDb())
        {
            var tenant = new Tenant($"Concurrent {Guid.NewGuid():N}");
            var plan = new Plan($"PLAN-{Guid.NewGuid():N}", "Concurrent plan");
            tenantId = tenant.Id;
            setupDb.AddRange(tenant, plan);
            setupDb.PlanEntitlements.Add(new PlanEntitlement(plan.Id, "resource.max", 1));
            setupDb.TenantPlans.Add(new TenantPlan(tenant.Id, plan.Id));
            await setupDb.SaveChangesAsync();
        }

        async Task<EntitlementLimitDecision> ReserveAsync()
        {
            await using var operationDb = fixture.CreateDb();
            var access = new EntitlementAccess(new PlanAccess(operationDb));
            return await new EntitlementLimitGuard(operationDb, access).TryReserveAsync(tenantId, "resource.max");
        }

        var decisions = await Task.WhenAll(ReserveAsync(), ReserveAsync());

        Assert.Single(decisions, x => x.Allowed);
        Assert.Single(decisions, x => x.Status == EntitlementLimitStatus.Exhausted);
        await using var verificationDb = fixture.CreateDb();
        Assert.Equal(1, (await verificationDb.EntitlementUsages.SingleAsync(x => x.TenantId == tenantId && x.Key == "RESOURCE.MAX")).Used);
    }

    [Fact]
    public async Task Plan_exposes_multiple_modules_with_core_baseline_without_billing_dependency()
    {
        await using var db = fixture.CreateDb();
        var tenant = new Tenant($"Modules {Guid.NewGuid():N}");
        var plan = new Plan($"PLAN-{Guid.NewGuid():N}", "Multi-module plan");
        plan.Modules.Add(new PlanModule(plan.Id, "SCHEDULING"));
        plan.Modules.Add(new PlanModule(plan.Id, "CLIENTS"));
        db.AddRange(tenant, plan, new TenantPlan(tenant.Id, plan.Id));
        await db.SaveChangesAsync();

        var modules = await new PlanAccess(db).GetAccessibleModuleCodesAsync(tenant.Id);

        Assert.Contains("CORE", modules);
        Assert.Contains("SCHEDULING", modules);
        Assert.Contains("CLIENTS", modules);
    }
}
