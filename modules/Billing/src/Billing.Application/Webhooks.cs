using System.Security.Cryptography;
using Billing.Domain;

namespace Billing.Application;

public enum ProviderWebhookStatus
{
    Pending = 0,
    Processed = 1,
    RetryScheduled = 2,
    Failed = 3,
    Processing = 4
}
public enum ProviderWebhookResult { Processed, Duplicate, RetryScheduled }

public sealed record ProviderWebhookRequest(
    string ProviderCode,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);

public sealed record NormalizedProviderEvent(
    string ProviderCode,
    string ExternalEventId,
    string EventType,
    string ExternalSubscriptionId,
    DateTime OccurredAtUtc,
    IReadOnlyDictionary<string, string> Data);

public sealed record ClaimedProviderEvent(NormalizedProviderEvent Notification, Guid ProcessingId);

public interface IProviderWebhookAdapter
{
    string ProviderCode { get; }
    Task<NormalizedProviderEvent> ValidateAndNormalizeAsync(ProviderWebhookRequest request, CancellationToken cancellationToken = default);
}

public interface IProviderWebhookInbox
{
    Task<bool> TryStoreAsync(NormalizedProviderEvent notification, string payloadHash, CancellationToken cancellationToken = default);
    Task<ClaimedProviderEvent?> ClaimAsync(string providerCode, string externalEventId, DateTime utcNow, TimeSpan lease, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<ClaimedProviderEvent>> ClaimDueAsync(DateTime utcNow, DateTime pendingBeforeUtc, TimeSpan lease, int limit = 50, CancellationToken cancellationToken = default);
    Task MarkProcessedAsync(string providerCode, string externalEventId, Guid processingId, CancellationToken cancellationToken = default);
    Task MarkRetryAsync(string providerCode, string externalEventId, Guid processingId, string reason, DateTime retryAtUtc, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(string providerCode, string externalEventId, Guid processingId, string reason, CancellationToken cancellationToken = default);
}

public interface IProviderEventHandler
{
    Task HandleAsync(NormalizedProviderEvent notification, CancellationToken cancellationToken = default);
}

public sealed class BillingWebhookProcessor(
    IEnumerable<IProviderWebhookAdapter> adapters,
    IProviderWebhookInbox inbox,
    IEnumerable<IProviderEventHandler> handlers,
    IBillingClock clock)
{
    public async Task<ProviderWebhookResult> ProcessAsync(ProviderWebhookRequest request, CancellationToken cancellationToken = default)
    {
        var adapter = adapters.SingleOrDefault(x => string.Equals(x.ProviderCode, request.ProviderCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Billing provider is not registered.");
        var notification = await adapter.ValidateAndNormalizeAsync(request, cancellationToken);
        BillingWebhookDataGuard.EnsureSafe(notification.Data);
        var payloadHash = Convert.ToHexString(SHA256.HashData(request.Body.Span));
        if (!await inbox.TryStoreAsync(notification, payloadHash, cancellationToken)) return ProviderWebhookResult.Duplicate;
        var claimed = await inbox.ClaimAsync(notification.ProviderCode, notification.ExternalEventId, clock.UtcNow, TimeSpan.FromMinutes(5), cancellationToken)
            ?? throw new InvalidOperationException("The stored billing event could not be claimed.");

        return await ProcessClaimedAsync(claimed, throwPermanentFailure: true, cancellationToken);
    }

    public async Task<int> RetryDueAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var notifications = await inbox.ClaimDueAsync(clock.UtcNow, clock.UtcNow.AddMinutes(-1), TimeSpan.FromMinutes(5), limit, cancellationToken);
        var processed = 0;
        foreach (var notification in notifications)
            if (await ProcessClaimedAsync(notification, throwPermanentFailure: false, cancellationToken) == ProviderWebhookResult.Processed) processed++;
        return processed;
    }

    private async Task<ProviderWebhookResult> ProcessClaimedAsync(ClaimedProviderEvent claimed, bool throwPermanentFailure, CancellationToken cancellationToken)
    {
        var notification = claimed.Notification;
        try
        {
            await HandleAsync(notification, cancellationToken);
            await inbox.MarkProcessedAsync(notification.ProviderCode, notification.ExternalEventId, claimed.ProcessingId, cancellationToken);
            return ProviderWebhookResult.Processed;
        }
        catch (TransientBillingProviderException exception)
        {
            await inbox.MarkRetryAsync(notification.ProviderCode, notification.ExternalEventId, claimed.ProcessingId,
                exception.Message, clock.UtcNow.AddMinutes(throwPermanentFailure ? 1 : 5), cancellationToken);
            return ProviderWebhookResult.RetryScheduled;
        }
        catch (Exception exception)
        {
            await inbox.MarkFailedAsync(notification.ProviderCode, notification.ExternalEventId, claimed.ProcessingId, exception.Message, cancellationToken);
            if (throwPermanentFailure) throw;
            return ProviderWebhookResult.RetryScheduled;
        }
    }

    private async Task HandleAsync(NormalizedProviderEvent notification, CancellationToken cancellationToken)
    {
        var registeredHandlers = handlers.ToArray();
        if (registeredHandlers.Length == 0)
            throw new InvalidOperationException("No billing provider event handler is registered.");

        foreach (var handler in registeredHandlers)
            await handler.HandleAsync(notification, cancellationToken);
    }
}

public static class BillingWebhookDataGuard
{
    public static void EnsureSafe(IReadOnlyDictionary<string, string> data)
    {
        try { BillingSensitiveDataGuard.EnsureSafeEntries(data); }
        catch (Shine.Domain.DomainException exception) { throw new InvalidOperationException("Normalized billing events cannot contain payment credentials.", exception); }
    }
}

public sealed class TransientBillingProviderException(string message, Exception? innerException = null) : Exception(message, innerException);

public interface IBillingReconciliationTarget
{
    Task ApplyProviderStateAsync(string providerCode, ProviderSubscriptionState state, CancellationToken cancellationToken = default);
}

public sealed class BillingReconciliationService(IEnumerable<IBillingProvider> providers, IBillingReconciliationTarget target)
{
    public async Task ReconcileAsync(string providerCode, string externalSubscriptionId, CancellationToken cancellationToken = default)
    {
        var provider = providers.SingleOrDefault(x => string.Equals(x.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException("Billing provider is not registered.");
        var state = await provider.GetSubscriptionAsync(externalSubscriptionId, cancellationToken);
        await target.ApplyProviderStateAsync(provider.ProviderCode, state, cancellationToken);
    }
}
