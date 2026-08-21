using Billing.Application;
using Billing.Domain;
using Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class BillingSubscriptionPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Activated_subscription_is_persisted_with_units_and_transactional_outbox_event()
    {
        var accountId = Guid.NewGuid();
        var planId = Guid.NewGuid();
        var unitId = Guid.NewGuid();
        var startsAtUtc = DateTime.UtcNow;

        await using (var db = fixture.CreateBillingDb())
        {
            var service = new SubscriptionActivationService(new SubscriptionRepository(db), new AllowAllOwnership(), new AllowAllPlanCatalog());
            await service.CreateAndActivateAsync(accountId, planId, [unitId],
                new BillingInterval(BillingIntervalUnit.Month, 1), startsAtUtc);
        }

        await using var verification = fixture.CreateBillingDb();
        var stored = await verification.Subscriptions.Include(x => x.Units)
            .SingleAsync(x => x.AccountId == accountId);
        var outbox = (await verification.OutboxMessages.AsNoTracking()
                .Where(x => x.EventType == nameof(SubscriptionActivated) && x.Status == BillingOutboxStatus.Pending)
                .ToArrayAsync())
            .Single(x => x.PayloadJson.Contains(stored.Id.ToString(), StringComparison.OrdinalIgnoreCase));

        Assert.Equal(SubscriptionStatus.Active, stored.Status);
        Assert.Equal(unitId, Assert.Single(stored.Units).UnitId);
        Assert.Contains(stored.Id.ToString(), outbox.PayloadJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Concurrent_activations_cannot_create_two_effective_subscriptions_for_the_same_unit()
    {
        var unitId = Guid.NewGuid();

        async Task<Exception?> ActivateAsync()
        {
            try
            {
                await using var db = fixture.CreateBillingDb();
                await new SubscriptionActivationService(new SubscriptionRepository(db), new AllowAllOwnership(), new AllowAllPlanCatalog()).CreateAndActivateAsync(
                    Guid.NewGuid(), Guid.NewGuid(), [unitId], new BillingInterval(BillingIntervalUnit.Month, 1), DateTime.UtcNow);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        }

        var results = await Task.WhenAll(ActivateAsync(), ActivateAsync());

        Assert.Single(results, x => x is null);
        Assert.Single(results, x => x is DomainException);
        await using var verification = fixture.CreateBillingDb();
        Assert.Equal(1, await verification.SubscriptionUnits.CountAsync(x => x.UnitId == unitId && x.IsEffective));
    }

    [Fact]
    public async Task Persisted_outbox_event_is_dispatched_and_marked_as_processed()
    {
        var accountId = Guid.NewGuid();
        await using var db = fixture.CreateBillingDb();
        await new SubscriptionActivationService(new SubscriptionRepository(db), new AllowAllOwnership(), new AllowAllPlanCatalog()).CreateAndActivateAsync(
            accountId, Guid.NewGuid(), [Guid.NewGuid()], new BillingInterval(BillingIntervalUnit.Month, 1), DateTime.UtcNow);

        var handler = new CapturingActivationHandler();
        var services = new ServiceCollection()
            .AddSingleton<IDomainEventHandler<SubscriptionActivated>>(handler)
            .BuildServiceProvider();
        var processed = await new BillingOutboxProcessor(db, services, new FixedClock(DateTime.UtcNow)).ProcessDueAsync();

        Assert.True(processed >= 1);
        Assert.Single(handler.Events, x => x.AccountId == accountId);
        Assert.Equal(BillingOutboxStatus.Processed,
            (await db.OutboxMessages.AsNoTracking().ToArrayAsync())
                .Single(x => x.EventType == nameof(SubscriptionActivated) && x.PayloadJson.Contains(accountId.ToString(), StringComparison.OrdinalIgnoreCase)).Status);
    }

    [Fact]
    public async Task Activation_rejects_a_unit_from_another_organization_before_persisting_the_subscription()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var expectedAccount = new CustomerAccount($"Expected {Guid.NewGuid():N}");
        var otherAccount = new CustomerAccount($"Other {Guid.NewGuid():N}");
        var unit = new Tenant($"Foreign unit {Guid.NewGuid():N}");
        var plan = new Shine.Domain.Plan($"OWNERSHIP-{Guid.NewGuid():N}", "Ownership validation plan");
        unit.AssignToCustomerAccount(otherAccount.Id);
        coreDb.AddRange(expectedAccount, otherAccount, unit, plan);
        await coreDb.SaveChangesAsync();

        var service = new SubscriptionActivationService(
            new SubscriptionRepository(billingDb), new SubscriptionUnitOwnership(coreDb), new SubscriptionPlanCatalog(coreDb));

        await Assert.ThrowsAsync<DomainException>(() => service.CreateAndActivateAsync(
            expectedAccount.Id, plan.Id, [unit.Id],
            new BillingInterval(BillingIntervalUnit.Month, 1), DateTime.UtcNow));
        Assert.False(await billingDb.Subscriptions.AnyAsync(x => x.AccountId == expectedAccount.Id));
    }

    [Fact]
    public async Task Activation_rejects_an_unknown_plan_before_persisting_the_subscription()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var account = new CustomerAccount($"Unknown plan {Guid.NewGuid():N}");
        var unit = new Tenant($"Unknown plan unit {Guid.NewGuid():N}");
        unit.AssignToCustomerAccount(account.Id);
        coreDb.AddRange(account, unit);
        await coreDb.SaveChangesAsync();
        var service = new SubscriptionActivationService(
            new SubscriptionRepository(billingDb), new SubscriptionUnitOwnership(coreDb), new SubscriptionPlanCatalog(coreDb));

        await Assert.ThrowsAsync<DomainException>(() => service.CreateAndActivateAsync(
            account.Id, Guid.NewGuid(), [unit.Id], new BillingInterval(BillingIntervalUnit.Month, 1), DateTime.UtcNow));

        Assert.False(await billingDb.Subscriptions.AnyAsync(x => x.AccountId == account.Id));
        Assert.DoesNotContain(await billingDb.OutboxMessages.AsNoTracking().ToArrayAsync(),
            x => x.PayloadJson.Contains(account.Id.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Plan_change_service_validates_requested_and_pending_plans_before_mutating_or_publishing()
    {
        var currentPlan = Guid.NewGuid();
        var requestedPlan = Guid.NewGuid();
        var effectiveAt = DateTime.UtcNow;
        Guid subscriptionId;
        await using (var db = fixture.CreateBillingDb())
        {
            var subscription = await new SubscriptionActivationService(new SubscriptionRepository(db), new AllowAllOwnership(), new AllowAllPlanCatalog())
                .CreateAndActivateAsync(Guid.NewGuid(), currentPlan, [Guid.NewGuid()], new BillingInterval(BillingIntervalUnit.Month, 1), effectiveAt);
            subscriptionId = subscription.Id;
        }

        await using (var db = fixture.CreateBillingDb())
        {
            var missingPlan = new SubscriptionPlanChangeService(new SubscriptionRepository(db), new SelectivePlanCatalog([]));
            await Assert.ThrowsAsync<DomainException>(() => missingPlan.RequestAsync(subscriptionId, requestedPlan, effectiveAt));
            var unchanged = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == subscriptionId);
            Assert.Null(unchanged.PendingPlanId);
            Assert.DoesNotContain(await db.OutboxMessages.AsNoTracking().ToArrayAsync(),
                x => x.EventType == nameof(SubscriptionPlanChangeRequested) && x.PayloadJson.Contains(requestedPlan.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        await using (var db = fixture.CreateBillingDb())
        {
            var validPlan = new SubscriptionPlanChangeService(new SubscriptionRepository(db), new SelectivePlanCatalog([requestedPlan]));
            await validPlan.RequestAsync(subscriptionId, requestedPlan, effectiveAt);
            Assert.Equal(requestedPlan, (await db.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == subscriptionId)).PendingPlanId);
        }

        await using (var db = fixture.CreateBillingDb())
        {
            var removedPlan = new SubscriptionPlanChangeService(new SubscriptionRepository(db), new SelectivePlanCatalog([]));
            await Assert.ThrowsAsync<DomainException>(() => removedPlan.ApplyAsync(subscriptionId, effectiveAt));
            var pending = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == subscriptionId);
            Assert.Equal(currentPlan, pending.PlanId);
            Assert.Equal(requestedPlan, pending.PendingPlanId);
            Assert.DoesNotContain(await db.OutboxMessages.AsNoTracking().ToArrayAsync(),
                x => x.EventType == nameof(SubscriptionPlanChanged) && x.PayloadJson.Contains(subscriptionId.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        await using (var db = fixture.CreateBillingDb())
        {
            var validPlan = new SubscriptionPlanChangeService(new SubscriptionRepository(db), new SelectivePlanCatalog([requestedPlan]));
            await validPlan.ApplyAsync(subscriptionId, effectiveAt);
            var changed = await db.Subscriptions.AsNoTracking().SingleAsync(x => x.Id == subscriptionId);
            Assert.Equal(requestedPlan, changed.PlanId);
            Assert.Null(changed.PendingPlanId);
            Assert.Contains(await db.OutboxMessages.AsNoTracking().ToArrayAsync(),
                x => x.EventType == nameof(SubscriptionPlanChanged) && x.PayloadJson.Contains(subscriptionId.ToString(), StringComparison.OrdinalIgnoreCase));
        }

        await using (var db = fixture.CreateBillingDb())
        {
            using var services = new ServiceCollection()
                .AddSingleton<IDomainEventHandler<SubscriptionPlanChanged>, FailingPlanProjection>()
                .BuildServiceProvider();
            await new BillingOutboxProcessor(db, services, new FixedClock(DateTime.UtcNow)).ProcessDueAsync(500);
            var failedProjection = (await db.OutboxMessages.AsNoTracking()
                    .Where(x => x.EventType == nameof(SubscriptionPlanChanged)).ToArrayAsync())
                .Single(x => x.PayloadJson.Contains(subscriptionId.ToString(), StringComparison.OrdinalIgnoreCase));
            Assert.Equal(BillingOutboxStatus.RetryScheduled, failedProjection.Status);
            Assert.NotNull(failedProjection.NextAttemptAtUtc);
        }
    }

    private sealed class CapturingActivationHandler : IDomainEventHandler<SubscriptionActivated>
    {
        public List<SubscriptionActivated> Events { get; } = [];
        public Task HandleAsync(SubscriptionActivated domainEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(domainEvent);
            return Task.CompletedTask;
        }
    }

    private sealed record FixedClock(DateTime UtcNow) : IBillingClock;

    private sealed class AllowAllOwnership : ISubscriptionUnitOwnership
    {
        public Task<bool> AllBelongToAccountAsync(Guid accountId, IReadOnlyCollection<Guid> unitIds,
            CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class AllowAllPlanCatalog : ISubscriptionPlanCatalog
    {
        public Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class SelectivePlanCatalog(IEnumerable<Guid> planIds) : ISubscriptionPlanCatalog
    {
        private readonly HashSet<Guid> plans = planIds.ToHashSet();
        public Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(plans.Contains(planId));
    }

    private sealed class FailingPlanProjection : IDomainEventHandler<SubscriptionPlanChanged>
    {
        public Task HandleAsync(SubscriptionPlanChanged domainEvent, CancellationToken cancellationToken = default) =>
            throw new TimeoutException("Projection temporarily unavailable.");
    }
}
