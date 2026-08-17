using Billing.Domain;
using Shine.Domain;

namespace Billing.Application;

public sealed class SubscriptionActivationService(
    ISubscriptionRepository subscriptions,
    ISubscriptionUnitOwnership ownership,
    ISubscriptionPlanCatalog plans)
{
    public async Task<Subscription> CreateAndActivateAsync(
        Guid accountId,
        Guid planId,
        IReadOnlyCollection<Guid> unitIds,
        BillingInterval interval,
        DateTime startsAtUtc,
        CancellationToken cancellationToken = default)
    {
        var units = unitIds.Distinct().ToArray();
        if (units.Length == 0) throw new DomainException("At least one unit is required.");
        if (!await plans.ExistsAsync(planId, cancellationToken))
            throw new DomainException("The subscription plan does not exist.");
        if (!await ownership.AllBelongToAccountAsync(accountId, units, cancellationToken))
            throw new DomainException("Every unit must belong to the subscription organization.");
        return await subscriptions.ExecuteUnitAssignmentAsync(units, async operationCancellationToken =>
        {
            foreach (var unitId in units)
                if (await subscriptions.HasEffectiveSubscriptionAsync(unitId, cancellationToken: operationCancellationToken))
                    throw new DomainException("The unit already has an effective subscription.");

            var subscription = new Subscription(accountId, planId, interval);
            foreach (var unitId in units) subscription.AddUnit(unitId);
            subscription.Activate(startsAtUtc);
            await subscriptions.AddAsync(subscription, operationCancellationToken);
            await subscriptions.SaveChangesAsync(operationCancellationToken);
            return subscription;
        }, cancellationToken);
    }
}
