using Shine.Domain;

namespace Billing.Domain;

public enum SubscriptionStatus
{
    Draft,
    Active,
    CancellationPending,
    Canceled
}

public enum BillingIntervalUnit
{
    Day,
    Month,
    Year
}

public readonly record struct BillingInterval
{
    public BillingInterval(BillingIntervalUnit unit, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        Unit = unit;
        Count = count;
    }

    public BillingIntervalUnit Unit { get; }
    public int Count { get; }

    public DateTime AddTo(DateTime utcDate) => Unit switch
    {
        BillingIntervalUnit.Day => utcDate.AddDays(Count),
        BillingIntervalUnit.Month => utcDate.AddMonths(Count),
        BillingIntervalUnit.Year => utcDate.AddYears(Count),
        _ => throw new ArgumentOutOfRangeException()
    };
}

public sealed class Subscription : BaseEntity<Guid>
{
    private readonly List<SubscriptionUnit> units = [];

    private Subscription() : base(Guid.Empty) { }

    public Subscription(Guid accountId, Guid planId, BillingInterval interval) : base(Guid.NewGuid())
    {
        if (accountId == Guid.Empty) throw new ArgumentException("Organization is required.", nameof(accountId));
        if (planId == Guid.Empty) throw new ArgumentException("Plan is required.", nameof(planId));
        AccountId = accountId;
        PlanId = planId;
        Interval = interval;
        Status = SubscriptionStatus.Draft;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid AccountId { get; private set; }
    public Guid PlanId { get; private set; }
    public Guid? PendingPlanId { get; private set; }
    public DateTime? PendingPlanEffectiveAtUtc { get; private set; }
    public BillingInterval Interval { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime? CurrentPeriodStartsAtUtc { get; private set; }
    public DateTime? CurrentPeriodEndsAtUtc { get; private set; }
    public DateTime? CancellationEffectiveAtUtc { get; private set; }
    public DateTime? CanceledAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public IReadOnlyCollection<SubscriptionUnit> Units => units.AsReadOnly();
    public IReadOnlySet<Guid> UnitIds => units.Select(x => x.UnitId).ToHashSet();

    public void AddUnit(Guid unitId)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("Unit is required.", nameof(unitId));
        if (Status != SubscriptionStatus.Draft) throw new DomainException("Units can only be changed before activation.");
        if (units.All(x => x.UnitId != unitId)) units.Add(new SubscriptionUnit(Id, unitId));
    }

    public void RemoveUnit(Guid unitId)
    {
        if (Status != SubscriptionStatus.Draft) throw new DomainException("Units can only be changed before activation.");
        units.RemoveAll(x => x.UnitId == unitId);
    }

    public void Activate(DateTime startsAtUtc)
    {
        EnsureUtc(startsAtUtc);
        if (Status != SubscriptionStatus.Draft) throw new DomainException("Only a draft subscription can be activated.");
        if (units.Count == 0) throw new DomainException("At least one unit is required.");

        Status = SubscriptionStatus.Active;
        foreach (var unit in units) unit.SetEffective(true);
        CurrentPeriodStartsAtUtc = startsAtUtc;
        CurrentPeriodEndsAtUtc = Interval.AddTo(startsAtUtc);
        AddDomainEvent(new SubscriptionActivated(Guid.NewGuid(), 1, CorrelationId(), Id, AccountId, PlanId, UnitIds.ToArray(), startsAtUtc, CurrentPeriodEndsAtUtc.Value, startsAtUtc));
    }

    public void RequestPlanChange(Guid planId, DateTime effectiveAtUtc)
    {
        if (planId == Guid.Empty) throw new ArgumentException("Plan is required.", nameof(planId));
        EnsureUtc(effectiveAtUtc);
        EnsureActive();
        if (planId == PlanId) throw new DomainException("The requested plan is already active.");
        if (effectiveAtUtc < CurrentPeriodStartsAtUtc) throw new DomainException("The plan change cannot precede the current period.");

        PendingPlanId = planId;
        PendingPlanEffectiveAtUtc = effectiveAtUtc;
        AddDomainEvent(new SubscriptionPlanChangeRequested(Guid.NewGuid(), 1, CorrelationId(), Id, AccountId, PlanId, planId, effectiveAtUtc, DateTime.UtcNow));
    }

    public void ApplyPendingPlanChange(DateTime utcNow)
    {
        EnsureUtc(utcNow);
        EnsureActive();
        if (PendingPlanId is not Guid planId || PendingPlanEffectiveAtUtc is not DateTime effectiveAtUtc || effectiveAtUtc > utcNow)
            throw new DomainException("There is no effective plan change to apply.");
        var previousPlanId = PlanId;
        PlanId = planId;
        PendingPlanId = null;
        PendingPlanEffectiveAtUtc = null;
        AddDomainEvent(new SubscriptionPlanChanged(Guid.NewGuid(), 1, CorrelationId(), Id, AccountId, previousPlanId, planId, UnitIds.ToArray(), utcNow));
    }

    public void RequestCancellation(bool atPeriodEnd, DateTime utcNow)
    {
        EnsureUtc(utcNow);
        EnsureActive();
        var effectiveAtUtc = atPeriodEnd ? CurrentPeriodEndsAtUtc!.Value : utcNow;
        Status = SubscriptionStatus.CancellationPending;
        CancellationEffectiveAtUtc = effectiveAtUtc;
        AddDomainEvent(new SubscriptionCancellationRequested(Guid.NewGuid(), 1, CorrelationId(), Id, AccountId, effectiveAtUtc, utcNow));
    }

    public void Cancel(DateTime utcNow)
    {
        EnsureUtc(utcNow);
        if (Status != SubscriptionStatus.CancellationPending || CancellationEffectiveAtUtc > utcNow)
            throw new DomainException("The subscription is not ready for cancellation.");
        Status = SubscriptionStatus.Canceled;
        foreach (var unit in units) unit.SetEffective(false);
        CanceledAtUtc = utcNow;
        AddDomainEvent(new SubscriptionCanceled(Guid.NewGuid(), 1, CorrelationId(), Id, AccountId, UnitIds.ToArray(), utcNow));
    }

    public void AdvancePeriod(DateTime utcNow)
    {
        EnsureUtc(utcNow);
        EnsureActive();
        if (CurrentPeriodEndsAtUtc > utcNow) throw new DomainException("The current period has not ended.");
        CurrentPeriodStartsAtUtc = CurrentPeriodEndsAtUtc;
        CurrentPeriodEndsAtUtc = Interval.AddTo(CurrentPeriodStartsAtUtc!.Value);
    }

    private void EnsureActive()
    {
        if (Status != SubscriptionStatus.Active) throw new DomainException("The subscription must be active.");
    }

    private static void EnsureUtc(DateTime value)
    {
        if (value.Kind != DateTimeKind.Utc) throw new ArgumentException("Billing dates must use UTC.");
    }

    private string CorrelationId() => $"subscription:{Id:N}";
}

public sealed class SubscriptionUnit
{
    private SubscriptionUnit() { }
    internal SubscriptionUnit(Guid subscriptionId, Guid unitId)
    {
        SubscriptionId = subscriptionId;
        UnitId = unitId;
    }

    public Guid SubscriptionId { get; private set; }
    public Guid UnitId { get; private set; }
    public bool IsEffective { get; private set; }
    public Subscription Subscription { get; private set; } = null!;
    internal void SetEffective(bool effective) => IsEffective = effective;
}

public interface ISubscriptionIntegrationEvent : IDomainEvent
{
    Guid EventId { get; }
    int Version { get; }
    string CorrelationId { get; }
    Guid SubscriptionId { get; }
    Guid AccountId { get; }
    DateTime OccurredAtUtc { get; }
}

public sealed record SubscriptionActivated(Guid EventId, int Version, string CorrelationId, Guid SubscriptionId, Guid AccountId, Guid PlanId, IReadOnlyCollection<Guid> UnitIds, DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime OccurredAtUtc) : ISubscriptionIntegrationEvent;
public sealed record SubscriptionPlanChangeRequested(Guid EventId, int Version, string CorrelationId, Guid SubscriptionId, Guid AccountId, Guid CurrentPlanId, Guid RequestedPlanId, DateTime EffectiveAtUtc, DateTime OccurredAtUtc) : ISubscriptionIntegrationEvent;
public sealed record SubscriptionPlanChanged(Guid EventId, int Version, string CorrelationId, Guid SubscriptionId, Guid AccountId, Guid PreviousPlanId, Guid CurrentPlanId, IReadOnlyCollection<Guid> UnitIds, DateTime OccurredAtUtc) : ISubscriptionIntegrationEvent;
public sealed record SubscriptionCancellationRequested(Guid EventId, int Version, string CorrelationId, Guid SubscriptionId, Guid AccountId, DateTime EffectiveAtUtc, DateTime OccurredAtUtc) : ISubscriptionIntegrationEvent;
public sealed record SubscriptionCanceled(Guid EventId, int Version, string CorrelationId, Guid SubscriptionId, Guid AccountId, IReadOnlyCollection<Guid> UnitIds, DateTime OccurredAtUtc) : ISubscriptionIntegrationEvent;
