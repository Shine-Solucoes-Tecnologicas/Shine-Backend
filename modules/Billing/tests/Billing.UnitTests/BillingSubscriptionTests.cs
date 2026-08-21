using Billing.Application;
using Billing.Domain;
using Shine.Domain;

namespace Shine.UnitTests;

public sealed class BillingSubscriptionTests
{
    [Fact]
    public void Subscription_requires_a_unit_before_activation_and_calculates_period()
    {
        var subscription = new Subscription(Guid.NewGuid(), Guid.NewGuid(), new BillingInterval(BillingIntervalUnit.Month, 1));
        Assert.Throws<DomainException>(() => subscription.Activate(Utc(2026, 8, 17)));

        subscription.AddUnit(Guid.NewGuid());
        subscription.Activate(Utc(2026, 8, 17));

        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal(Utc(2026, 9, 17), subscription.CurrentPeriodEndsAtUtc);
        Assert.Contains(subscription.DomainEvents, x => x is SubscriptionActivated);
    }

    [Fact]
    public void Different_units_can_be_split_across_independent_subscriptions_and_plans()
    {
        var accountId = Guid.NewGuid();
        var first = new Subscription(accountId, Guid.NewGuid(), new BillingInterval(BillingIntervalUnit.Month, 1));
        var second = new Subscription(accountId, Guid.NewGuid(), new BillingInterval(BillingIntervalUnit.Year, 1));
        first.AddUnit(Guid.NewGuid());
        second.AddUnit(Guid.NewGuid());

        Assert.NotEqual(first.PlanId, second.PlanId);
        Assert.Empty(first.UnitIds.Intersect(second.UnitIds));
    }

    [Fact]
    public void Plan_change_is_scheduled_and_applied_only_when_effective()
    {
        var currentPlanId = Guid.NewGuid();
        var requestedPlanId = Guid.NewGuid();
        var subscription = ActiveSubscription(currentPlanId);
        var effectiveAt = subscription.CurrentPeriodEndsAtUtc!.Value;
        subscription.RequestPlanChange(requestedPlanId, effectiveAt);

        Assert.Throws<DomainException>(() => subscription.ApplyPendingPlanChange(effectiveAt.AddSeconds(-1)));
        subscription.ApplyPendingPlanChange(effectiveAt);

        Assert.Equal(requestedPlanId, subscription.PlanId);
        Assert.Null(subscription.PendingPlanId);
        Assert.Contains(subscription.DomainEvents, x => x is SubscriptionPlanChanged);
    }

    [Fact]
    public void Cancellation_at_period_end_does_not_cancel_early()
    {
        var subscription = ActiveSubscription(Guid.NewGuid());
        var requestedAt = subscription.CurrentPeriodStartsAtUtc!.Value.AddDays(1);
        subscription.RequestCancellation(atPeriodEnd: true, requestedAt);

        Assert.Equal(SubscriptionStatus.CancellationPending, subscription.Status);
        Assert.Throws<DomainException>(() => subscription.Cancel(subscription.CurrentPeriodEndsAtUtc!.Value.AddSeconds(-1)));
        subscription.Cancel(subscription.CurrentPeriodEndsAtUtc!.Value);
        Assert.Equal(SubscriptionStatus.Canceled, subscription.Status);
    }

    [Fact]
    public void Billing_dates_must_be_utc()
    {
        var subscription = new Subscription(Guid.NewGuid(), Guid.NewGuid(), new BillingInterval(BillingIntervalUnit.Day, 30));
        subscription.AddUnit(Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => subscription.Activate(DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Local)));
    }

    [Fact]
    public async Task Activation_service_rejects_a_second_effective_subscription_for_the_same_unit()
    {
        var unitId = Guid.NewGuid();
        var repository = new TestSubscriptionRepository(unitId);
        var service = new SubscriptionActivationService(repository, new TestOwnership(), new TestPlanCatalog());

        await Assert.ThrowsAsync<DomainException>(() => service.CreateAndActivateAsync(
            Guid.NewGuid(), Guid.NewGuid(), [unitId], new BillingInterval(BillingIntervalUnit.Month, 1), Utc(2026, 8, 17)));
        Assert.Empty(repository.Added);
    }

    [Fact]
    public async Task Activation_service_rejects_an_unknown_plan_before_writing()
    {
        var repository = new TestSubscriptionRepository();
        var service = new SubscriptionActivationService(repository, new TestOwnership(), new TestPlanCatalog(false));

        await Assert.ThrowsAsync<DomainException>(() => service.CreateAndActivateAsync(
            Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()],
            new BillingInterval(BillingIntervalUnit.Month, 1), Utc(2026, 8, 17)));

        Assert.Empty(repository.Added);
    }

    private static Subscription ActiveSubscription(Guid planId)
    {
        var subscription = new Subscription(Guid.NewGuid(), planId, new BillingInterval(BillingIntervalUnit.Month, 1));
        subscription.AddUnit(Guid.NewGuid());
        subscription.Activate(Utc(2026, 8, 17));
        return subscription;
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class TestSubscriptionRepository(params Guid[] occupiedUnits) : ISubscriptionRepository
    {
        private readonly HashSet<Guid> occupied = [.. occupiedUnits];
        public List<Subscription> Added { get; } = [];
        public Task<Subscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default) => Task.FromResult<Subscription?>(null);
        public Task<bool> HasEffectiveSubscriptionAsync(Guid unitId, Guid? excludingSubscriptionId = null, CancellationToken cancellationToken = default) => Task.FromResult(occupied.Contains(unitId));
        public Task AddAsync(Subscription subscription, CancellationToken cancellationToken = default) { Added.Add(subscription); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<T> ExecuteUnitAssignmentAsync<T>(IReadOnlyCollection<Guid> unitIds, Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default) => operation(cancellationToken);
    }

    private sealed class TestOwnership(bool result = true) : ISubscriptionUnitOwnership
    {
        public Task<bool> AllBelongToAccountAsync(Guid accountId, IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class TestPlanCatalog(bool result = true) : ISubscriptionPlanCatalog
    {
        public Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }
}
