using Billing.Domain;
using Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Billing.Application;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class BillingEntitlementProjectionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Activation_event_applies_plan_once_when_reprocessed()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var account = new CustomerAccount($"Billing {Guid.NewGuid():N}");
        var unit = new Tenant($"Billing unit {Guid.NewGuid():N}");
        var plan = new Shine.Domain.Plan($"BILL-{Guid.NewGuid():N}", "Billing plan");
        unit.AssignToCustomerAccount(account.Id);
        coreDb.AddRange(account, unit, plan);
        await coreDb.SaveChangesAsync();
        var item = new SubscriptionActivated(Guid.NewGuid(), 1, "billing-test", Guid.NewGuid(), account.Id, plan.Id,
            [unit.Id], DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), DateTime.UtcNow);
        var handler = new SubscriptionActivatedHandler(billingDb, coreDb, new PlanAccess(coreDb), new BackgroundExecutionContext());

        await handler.HandleAsync(item);
        await handler.HandleAsync(item);

        Assert.Equal(plan.Id, (await coreDb.TenantPlans.SingleAsync(x => x.TenantId == unit.Id)).PlanId);
        Assert.Equal(1, await billingDb.ProcessedEvents.CountAsync(x => x.EventId == item.EventId));
    }

    [Fact]
    public async Task Projection_rejects_a_unit_from_another_organization()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var account = new CustomerAccount($"Billing {Guid.NewGuid():N}");
        var otherAccount = new CustomerAccount($"Other billing {Guid.NewGuid():N}");
        var unit = new Tenant($"Other billing unit {Guid.NewGuid():N}");
        var plan = new Shine.Domain.Plan($"BILL-{Guid.NewGuid():N}", "Billing plan");
        unit.AssignToCustomerAccount(otherAccount.Id);
        coreDb.AddRange(account, otherAccount, unit, plan);
        await coreDb.SaveChangesAsync();
        var item = new SubscriptionActivated(Guid.NewGuid(), 1, "billing-isolation", Guid.NewGuid(), account.Id, plan.Id,
            [unit.Id], DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), DateTime.UtcNow);
        var handler = new SubscriptionActivatedHandler(billingDb, coreDb, new PlanAccess(coreDb), new BackgroundExecutionContext());

        await Assert.ThrowsAsync<Shine.Domain.DomainException>(() => handler.HandleAsync(item));
        Assert.False(await coreDb.TenantPlans.AnyAsync(x => x.TenantId == unit.Id));
        Assert.False(await billingDb.ProcessedEvents.AnyAsync(x => x.EventId == item.EventId));
    }

    [Fact]
    public async Task Concurrent_duplicate_event_produces_a_single_projection()
    {
        Guid accountId;
        Guid unitId;
        Guid planId;
        await using (var setupDb = fixture.CreateDb())
        {
            var account = new CustomerAccount($"Concurrent billing {Guid.NewGuid():N}");
            var unit = new Tenant($"Concurrent billing unit {Guid.NewGuid():N}");
            var plan = new Shine.Domain.Plan($"BILL-{Guid.NewGuid():N}", "Concurrent billing plan");
            accountId = account.Id; unitId = unit.Id; planId = plan.Id;
            unit.AssignToCustomerAccount(account.Id);
            setupDb.AddRange(account, unit, plan);
            await setupDb.SaveChangesAsync();
        }
        var item = new SubscriptionActivated(Guid.NewGuid(), 1, "billing-concurrent", Guid.NewGuid(), accountId, planId,
            [unitId], DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), DateTime.UtcNow);

        async Task HandleAsync()
        {
            await using var coreDb = fixture.CreateDb();
            await using var billingDb = fixture.CreateBillingDb();
            await new SubscriptionActivatedHandler(billingDb, coreDb, new PlanAccess(coreDb), new BackgroundExecutionContext()).HandleAsync(item);
        }

        await Task.WhenAll(HandleAsync(), HandleAsync());

        await using var verificationCore = fixture.CreateDb();
        await using var verificationBilling = fixture.CreateBillingDb();
        Assert.Single(await verificationCore.TenantPlans.Where(x => x.TenantId == unitId).ToArrayAsync());
        Assert.Single(await verificationBilling.ProcessedEvents.Where(x => x.EventId == item.EventId).ToArrayAsync());
    }

    [Fact]
    public async Task Units_from_the_same_organization_can_receive_different_plans()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var account = new CustomerAccount($"Multi-plan {Guid.NewGuid():N}");
        var firstUnit = new Tenant($"Multi-plan A {Guid.NewGuid():N}");
        var secondUnit = new Tenant($"Multi-plan B {Guid.NewGuid():N}");
        var firstPlan = new Shine.Domain.Plan($"PLAN-A-{Guid.NewGuid():N}", "Plan A");
        var secondPlan = new Shine.Domain.Plan($"PLAN-B-{Guid.NewGuid():N}", "Plan B");
        firstUnit.AssignToCustomerAccount(account.Id);
        secondUnit.AssignToCustomerAccount(account.Id);
        coreDb.AddRange(account, firstUnit, secondUnit, firstPlan, secondPlan);
        await coreDb.SaveChangesAsync();
        var handler = new SubscriptionActivatedHandler(billingDb, coreDb, new PlanAccess(coreDb), new BackgroundExecutionContext());

        await handler.HandleAsync(new SubscriptionActivated(Guid.NewGuid(), 1, "multi-plan-a", Guid.NewGuid(), account.Id,
            firstPlan.Id, [firstUnit.Id], DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), DateTime.UtcNow));
        await handler.HandleAsync(new SubscriptionActivated(Guid.NewGuid(), 1, "multi-plan-b", Guid.NewGuid(), account.Id,
            secondPlan.Id, [secondUnit.Id], DateTime.UtcNow, DateTime.UtcNow.AddMonths(1), DateTime.UtcNow));

        Assert.Equal(firstPlan.Id, (await coreDb.TenantPlans.SingleAsync(x => x.TenantId == firstUnit.Id)).PlanId);
        Assert.Equal(secondPlan.Id, (await coreDb.TenantPlans.SingleAsync(x => x.TenantId == secondUnit.Id)).PlanId);
    }

    [Fact]
    public async Task Persisted_plan_change_is_projected_through_the_outbox_and_replaces_entitlements()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var account = new CustomerAccount($"Plan change {Guid.NewGuid():N}");
        var unit = new Tenant($"Plan change unit {Guid.NewGuid():N}");
        var currentPlan = new Shine.Domain.Plan($"CURRENT-{Guid.NewGuid():N}", "Current plan");
        var nextPlan = new Shine.Domain.Plan($"NEXT-{Guid.NewGuid():N}", "Next plan");
        unit.AssignToCustomerAccount(account.Id);
        coreDb.AddRange(account, unit, currentPlan, nextPlan,
            new Shine.Domain.PlanEntitlement(currentPlan.Id, Shine.Domain.EntitlementKeys.SchedulingActiveAppointments, 3),
            new Shine.Domain.PlanEntitlement(nextPlan.Id, Shine.Domain.EntitlementKeys.SchedulingActiveAppointments, 10),
            new Shine.Domain.TenantPlan(unit.Id, currentPlan.Id));
        await coreDb.SaveChangesAsync();

        var effectiveAt = DateTime.UtcNow;
        var subscription = new Subscription(account.Id, currentPlan.Id,
            new BillingInterval(BillingIntervalUnit.Month, 1));
        subscription.AddUnit(unit.Id);
        subscription.Activate(effectiveAt);
        subscription.ClearDomainEvents();
        subscription.RequestPlanChange(nextPlan.Id, effectiveAt);
        subscription.ApplyPendingPlanChange(effectiveAt);
        billingDb.Subscriptions.Add(subscription);
        await billingDb.SaveChangesAsync();

        var handler = new SubscriptionPlanChangedHandler(billingDb, coreDb, new PlanAccess(coreDb), new BackgroundExecutionContext());
        using var services = new ServiceCollection()
            .AddSingleton<Shine.Infrastructure.IDomainEventHandler<SubscriptionPlanChanged>>(handler)
            .BuildServiceProvider();
        await new BillingOutboxProcessor(billingDb, services, new FixedClock(DateTime.UtcNow)).ProcessDueAsync(500);

        coreDb.ChangeTracker.Clear();
        Assert.Equal(nextPlan.Id, (await coreDb.TenantPlans.SingleAsync(x => x.TenantId == unit.Id)).PlanId);
        var entitlements = await new EntitlementAccess(new PlanAccess(coreDb)).GetAsync(unit.Id);
        Assert.Equal(10, entitlements.Entitlements[Shine.Domain.EntitlementKeys.SchedulingActiveAppointments].Value);
    }

    [Fact]
    public async Task Background_outbox_projects_activation_change_and_cancellation_with_real_tenant_context()
    {
        Guid accountId;
        Guid firstUnitId;
        Guid secondUnitId;
        Guid firstPlanId;
        Guid secondPlanId;
        await using (var setup = fixture.CreateDb())
        {
            var account = new CustomerAccount($"Worker account {Guid.NewGuid():N}");
            var firstUnit = new Tenant($"Worker unit A {Guid.NewGuid():N}");
            var secondUnit = new Tenant($"Worker unit B {Guid.NewGuid():N}");
            var firstPlan = new Shine.Domain.Plan($"WORKER-A-{Guid.NewGuid():N}", "Worker plan A");
            var secondPlan = new Shine.Domain.Plan($"WORKER-B-{Guid.NewGuid():N}", "Worker plan B");
            firstUnit.AssignToCustomerAccount(account.Id);
            secondUnit.AssignToCustomerAccount(account.Id);
            accountId = account.Id;
            firstUnitId = firstUnit.Id;
            secondUnitId = secondUnit.Id;
            firstPlanId = firstPlan.Id;
            secondPlanId = secondPlan.Id;
            setup.AddRange(account, firstUnit, secondUnit, firstPlan, secondPlan);
            await setup.SaveChangesAsync();
        }

        var backgroundTenant = new BackgroundTenant();
        var execution = new TenantExecutionContext(backgroundTenant);
        await using var coreDb = fixture.CreateDb(backgroundTenant, execution);
        await using var billingDb = fixture.CreateBillingDb();
        var activated = new SubscriptionActivatedHandler(billingDb, coreDb, new PlanAccess(coreDb), execution);
        var changed = new SubscriptionPlanChangedHandler(billingDb, coreDb, new PlanAccess(coreDb), execution);
        var canceled = new SubscriptionCanceledHandler(billingDb, coreDb, new PlanAccess(coreDb), execution);
        using var services = new ServiceCollection()
            .AddSingleton<Shine.Infrastructure.IDomainEventHandler<SubscriptionActivated>>(activated)
            .AddSingleton<Shine.Infrastructure.IDomainEventHandler<SubscriptionPlanChanged>>(changed)
            .AddSingleton<Shine.Infrastructure.IDomainEventHandler<SubscriptionCanceled>>(canceled)
            .BuildServiceProvider();
        var processor = new BillingOutboxProcessor(billingDb, services, new FixedClock(DateTime.UtcNow));
        var now = DateTime.UtcNow;
        var subscription = new Subscription(accountId, firstPlanId, new BillingInterval(BillingIntervalUnit.Month, 1));
        subscription.AddUnit(firstUnitId);
        subscription.AddUnit(secondUnitId);
        subscription.Activate(now);
        billingDb.Subscriptions.Add(subscription);
        await billingDb.SaveChangesAsync();

        await processor.ProcessDueAsync(500);
        Assert.Null(execution.EffectiveTenantId);
        Assert.False(await coreDb.TenantPlans.AnyAsync());
        await AssertPlanAsync(firstUnitId, firstPlanId);
        await AssertPlanAsync(secondUnitId, firstPlanId);

        var planChanges = new SubscriptionPlanChangeService(
            new SubscriptionRepository(billingDb),
            new ExistingPlanCatalog(secondPlanId));
        await planChanges.RequestAsync(subscription.Id, secondPlanId, now);
        await planChanges.ApplyAsync(subscription.Id, now);
        await processor.ProcessDueAsync(500);
        Assert.Null(execution.EffectiveTenantId);
        await AssertPlanAsync(firstUnitId, secondPlanId);
        await AssertPlanAsync(secondUnitId, secondPlanId);

        subscription = await billingDb.Subscriptions.Include(x => x.Units).SingleAsync(x => x.Id == subscription.Id);
        subscription.RequestCancellation(false, now);
        subscription.Cancel(now);
        await billingDb.SaveChangesAsync();
        await processor.ProcessDueAsync(500);
        Assert.Null(execution.EffectiveTenantId);
        using (execution.EnterTenant(firstUnitId)) Assert.False(await coreDb.TenantPlans.AnyAsync());
        using (execution.EnterTenant(secondUnitId)) Assert.False(await coreDb.TenantPlans.AnyAsync());

        async Task AssertPlanAsync(Guid unitId, Guid expectedPlanId)
        {
            using var scope = execution.EnterTenant(unitId);
            Assert.Equal(expectedPlanId, (await coreDb.TenantPlans.SingleAsync()).PlanId);
        }
    }

    private sealed record FixedClock(DateTime UtcNow) : IBillingClock;
    private sealed class ExistingPlanCatalog(params Guid[] planIds) : ISubscriptionPlanCatalog
    {
        private readonly HashSet<Guid> ids = planIds.ToHashSet();
        public Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(ids.Contains(planId));
    }

    private sealed class BackgroundTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public Guid? UserTenantId => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => false;
    }

    private sealed class BackgroundExecutionContext : ITenantExecutionContext
    {
        private Guid? tenantId;
        public Guid TenantId => tenantId ?? throw new Shine.Domain.TenantIsolationException("No active tenant.");
        public Guid? EffectiveTenantId => tenantId;
        public bool IsBypass => false;
        public IDisposable EnterTenant(Guid selectedTenantId)
        {
            var previous = tenantId;
            tenantId = selectedTenantId;
            return new Scope(() => tenantId = previous);
        }
        public IDisposable EnterBypass() => throw new Shine.Domain.TenantIsolationException("Bypass is not available.");
        private sealed class Scope(Action dispose) : IDisposable { public void Dispose() => dispose(); }
    }
}
