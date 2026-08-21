using Billing.Domain;
using Shine.Domain;

namespace Billing.Application;

public sealed class SubscriptionPlanChangeService(
    ISubscriptionRepository subscriptions,
    ISubscriptionPlanCatalog plans)
{
    public async Task<Subscription> RequestAsync(Guid subscriptionId, Guid requestedPlanId, DateTime effectiveAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!await plans.ExistsAsync(requestedPlanId, cancellationToken))
            throw new DomainException("The requested subscription plan does not exist.");
        var subscription = await subscriptions.GetAsync(subscriptionId, cancellationToken)
            ?? throw new DomainException("The subscription does not exist.");
        subscription.RequestPlanChange(requestedPlanId, effectiveAtUtc);
        await subscriptions.SaveChangesAsync(cancellationToken);
        return subscription;
    }

    public async Task<Subscription> ApplyAsync(Guid subscriptionId, DateTime utcNow,
        CancellationToken cancellationToken = default)
    {
        var subscription = await subscriptions.GetAsync(subscriptionId, cancellationToken)
            ?? throw new DomainException("The subscription does not exist.");
        if (subscription.PendingPlanId is not Guid pendingPlanId)
            throw new DomainException("There is no pending plan change.");
        if (!await plans.ExistsAsync(pendingPlanId, cancellationToken))
            throw new DomainException("The pending subscription plan does not exist.");
        subscription.ApplyPendingPlanChange(utcNow);
        await subscriptions.SaveChangesAsync(cancellationToken);
        return subscription;
    }
}
