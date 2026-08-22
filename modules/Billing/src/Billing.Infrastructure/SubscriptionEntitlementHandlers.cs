using Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Billing.Infrastructure;

public sealed class SubscriptionActivatedHandler(BillingDbContext billingDb, ShineDbContext coreDb, IPlanAccess plans, ITenantExecutionContext tenantExecutionContext)
    : IDomainEventHandler<SubscriptionActivated>
{
    public Task HandleAsync(SubscriptionActivated domainEvent, CancellationToken cancellationToken = default) =>
        BillingEventProjection.ApplyOnceAsync(billingDb, coreDb, domainEvent, domainEvent.UnitIds,
            (unitId, ct) => plans.SetPlanAsync(unitId, domainEvent.PlanId, ct), tenantExecutionContext, cancellationToken);
}

public sealed class SubscriptionPlanChangedHandler(BillingDbContext billingDb, ShineDbContext coreDb, IPlanAccess plans, ITenantExecutionContext tenantExecutionContext)
    : IDomainEventHandler<SubscriptionPlanChanged>
{
    public Task HandleAsync(SubscriptionPlanChanged domainEvent, CancellationToken cancellationToken = default) =>
        BillingEventProjection.ApplyOnceAsync(billingDb, coreDb, domainEvent, domainEvent.UnitIds,
            (unitId, ct) => plans.SetPlanAsync(unitId, domainEvent.CurrentPlanId, ct), tenantExecutionContext, cancellationToken);
}

public sealed class SubscriptionCanceledHandler(BillingDbContext billingDb, ShineDbContext coreDb, IPlanAccess plans, ITenantExecutionContext tenantExecutionContext)
    : IDomainEventHandler<SubscriptionCanceled>
{
    public Task HandleAsync(SubscriptionCanceled domainEvent, CancellationToken cancellationToken = default) =>
        BillingEventProjection.ApplyOnceAsync(billingDb, coreDb, domainEvent, domainEvent.UnitIds,
            plans.RemovePlanAsync, tenantExecutionContext, cancellationToken);
}

internal static class BillingEventProjection
{
    public static async Task ApplyOnceAsync<TEvent>(BillingDbContext billingDb, ShineDbContext coreDb,
        TEvent domainEvent, IReadOnlyCollection<Guid> unitIds, Func<Guid, CancellationToken, Task> apply,
        ITenantExecutionContext tenantExecutionContext, CancellationToken cancellationToken) where TEvent : ISubscriptionIntegrationEvent
    {
        if (domainEvent.Version != 1) throw new DomainException("The subscription event version is not supported.");
        await using var transaction = await billingDb.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"billing-event:{domainEvent.EventId:N}";
        await billingDb.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        if (await billingDb.ProcessedEvents.AnyAsync(x => x.EventId == domainEvent.EventId, cancellationToken))
        {
            await transaction.CommitAsync(cancellationToken);
            return;
        }
        var distinctUnits = unitIds.Distinct().ToArray();
        var ownedUnits = await coreDb.Tenants.AsNoTracking()
            .CountAsync(x => distinctUnits.Contains(x.Id) && x.CustomerAccountId == domainEvent.AccountId, cancellationToken);
        if (ownedUnits != distinctUnits.Length) throw new DomainException("A subscription event contains a unit outside its organization.");

        foreach (var unitId in distinctUnits)
        {
            using var tenantScope = tenantExecutionContext.EnterTenant(unitId);
            await apply(unitId, cancellationToken);
        }
        billingDb.ProcessedEvents.Add(new ProcessedBillingEvent(domainEvent.EventId, typeof(TEvent).Name,
            domainEvent.CorrelationId, DateTime.UtcNow));
        await billingDb.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
