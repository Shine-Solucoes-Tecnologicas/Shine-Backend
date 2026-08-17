using System.Text.Json;
using Billing.Application;
using Microsoft.EntityFrameworkCore;

namespace Billing.Infrastructure;

public sealed class ProviderWebhookInbox(BillingDbContext db) : IProviderWebhookInbox
{
    public async Task<bool> TryStoreAsync(NormalizedProviderEvent notification, string payloadHash, CancellationToken cancellationToken = default)
    {
        var provider = notification.ProviderCode.Trim().ToUpperInvariant();
        if (await db.ProviderWebhookInbox.AnyAsync(x => x.ProviderCode == provider && x.ExternalEventId == notification.ExternalEventId, cancellationToken)) return false;
        db.ProviderWebhookInbox.Add(new ProviderWebhookInboxItem(provider, notification.ExternalEventId, notification.EventType,
            notification.ExternalSubscriptionId, notification.OccurredAtUtc, JsonSerializer.Serialize(notification.Data), payloadHash));
        try { await db.SaveChangesAsync(cancellationToken); return true; }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            if (await db.ProviderWebhookInbox.AnyAsync(x => x.ProviderCode == provider && x.ExternalEventId == notification.ExternalEventId, cancellationToken)) return false;
            throw;
        }
    }

    public async Task<ClaimedProviderEvent?> ClaimAsync(string providerCode, string externalEventId, DateTime utcNow, TimeSpan lease, CancellationToken cancellationToken = default)
    {
        var provider = providerCode.Trim().ToUpperInvariant();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var lockKey = $"billing-webhook:{provider}:{externalEventId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        var item = await db.ProviderWebhookInbox.SingleOrDefaultAsync(x => x.ProviderCode == provider && x.ExternalEventId == externalEventId, cancellationToken);
        if (item is null || item.Status != ProviderWebhookStatus.Pending) return null;
        var processingId = item.Claim(utcNow, lease);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ToClaim(item, processingId);
    }

    public Task MarkProcessedAsync(string providerCode, string externalEventId, Guid processingId, CancellationToken cancellationToken = default) =>
        UpdateAsync(providerCode, externalEventId, processingId, x => x.MarkProcessed(processingId, DateTime.UtcNow), cancellationToken);
    public Task MarkRetryAsync(string providerCode, string externalEventId, Guid processingId, string reason, DateTime retryAtUtc, CancellationToken cancellationToken = default) =>
        UpdateAsync(providerCode, externalEventId, processingId, x => x.MarkRetry(processingId, reason, retryAtUtc), cancellationToken);
    public Task MarkFailedAsync(string providerCode, string externalEventId, Guid processingId, string reason, CancellationToken cancellationToken = default) =>
        UpdateAsync(providerCode, externalEventId, processingId, x => x.MarkFailed(processingId, reason), cancellationToken);

    public async Task<IReadOnlyCollection<ClaimedProviderEvent>> ClaimDueAsync(DateTime utcNow, DateTime pendingBeforeUtc, TimeSpan lease, int limit = 50, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var pending = (int)ProviderWebhookStatus.Pending;
        var retry = (int)ProviderWebhookStatus.RetryScheduled;
        var processing = (int)ProviderWebhookStatus.Processing;
        var items = await db.ProviderWebhookInbox.FromSqlInterpolated($$"""
            SELECT * FROM "BillingProviderWebhookInbox"
            WHERE ("Status" = {{pending}} AND "ReceivedAtUtc" <= {{pendingBeforeUtc}})
               OR ("Status" = {{retry}} AND "NextRetryAtUtc" <= {{utcNow}})
               OR ("Status" = {{processing}} AND "LockedUntilUtc" <= {{utcNow}})
            ORDER BY COALESCE("NextRetryAtUtc", "ReceivedAtUtc")
            FOR UPDATE SKIP LOCKED
            LIMIT {{limit}}
            """).ToArrayAsync(cancellationToken);
        var claims = items.Select(item => ToClaim(item, item.Claim(utcNow, lease))).ToArray();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    private async Task UpdateAsync(string providerCode, string externalEventId, Guid processingId, Action<ProviderWebhookInboxItem> update, CancellationToken cancellationToken)
    {
        var provider = providerCode.Trim().ToUpperInvariant();
        var item = await db.ProviderWebhookInbox.SingleAsync(x => x.ProviderCode == provider && x.ExternalEventId == externalEventId, cancellationToken);
        if (!item.IsOwnedBy(processingId)) return;
        update(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static ClaimedProviderEvent ToClaim(ProviderWebhookInboxItem item, Guid processingId) => new(
        new NormalizedProviderEvent(item.ProviderCode, item.ExternalEventId, item.EventType, item.ExternalSubscriptionId,
            item.OccurredAtUtc, JsonSerializer.Deserialize<Dictionary<string, string>>(item.DataJson) ?? []), processingId);
}

public sealed class SystemBillingClock : IBillingClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
