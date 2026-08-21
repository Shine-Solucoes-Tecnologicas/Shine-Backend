using Billing.Domain;

namespace Billing.Application;

public interface ISubscriptionRepository
{
    Task<Subscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    Task<bool> HasEffectiveSubscriptionAsync(Guid unitId, Guid? excludingSubscriptionId = null, CancellationToken cancellationToken = default);
    Task AddAsync(Subscription subscription, CancellationToken cancellationToken = default);
    Task SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<T> ExecuteUnitAssignmentAsync<T>(IReadOnlyCollection<Guid> unitIds,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
}

public interface ISubscriptionUnitOwnership
{
    Task<bool> AllBelongToAccountAsync(Guid accountId, IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken = default);
}

public interface ISubscriptionPlanCatalog
{
    Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default);
}

public interface IBillingProvider
{
    string ProviderCode { get; }
    Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken = default);
    Task<ProviderChargeResult> CreateChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken = default);
    Task CancelAsync(string externalSubscriptionId, CancellationToken cancellationToken = default);
    Task<ProviderSubscriptionState> GetSubscriptionAsync(string externalSubscriptionId, CancellationToken cancellationToken = default);
}

public interface IBillingClock
{
    DateTime UtcNow { get; }
}

public sealed record ProviderCheckoutRequest(Guid SubscriptionId, Guid AccountId, Guid PlanId, string SuccessUrl, string CancelUrl, string IdempotencyKey);
public sealed record ProviderCheckoutResult(string ExternalSubscriptionId, Uri CheckoutUrl);
public sealed record ProviderChargeRequest(Guid SubscriptionId, long AmountMinor, string Currency, string IdempotencyKey);
public sealed record ProviderChargeResult(string ExternalChargeId, string Status);
public sealed record ProviderSubscriptionState(string ExternalSubscriptionId, string Status, DateTime ObservedAtUtc);
