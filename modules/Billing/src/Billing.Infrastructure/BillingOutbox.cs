using System.Text.Json;
using Billing.Domain;

namespace Billing.Infrastructure;

public enum BillingOutboxStatus { Pending, Processing, Processed, RetryScheduled, Failed }

public sealed class BillingOutboxMessage
{
    private BillingOutboxMessage() { }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string PayloadJson { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }
    public BillingOutboxStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public Guid? ProcessingId { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static BillingOutboxMessage From(ISubscriptionIntegrationEvent domainEvent) => new()
    {
        EventId = domainEvent.EventId,
        EventType = domainEvent.GetType().Name,
        PayloadJson = JsonSerializer.Serialize(domainEvent, domainEvent.GetType()),
        OccurredAtUtc = domainEvent.OccurredAtUtc,
        Status = BillingOutboxStatus.Pending
    };

    public Guid Claim(DateTime utcNow, TimeSpan lease)
    {
        Status = BillingOutboxStatus.Processing;
        NextAttemptAtUtc = null;
        ProcessingId = Guid.NewGuid();
        LockedUntilUtc = utcNow.Add(lease);
        Attempts++;
        return ProcessingId.Value;
    }

    public bool IsOwnedBy(Guid processingId) => Status == BillingOutboxStatus.Processing && ProcessingId == processingId;
    public void MarkProcessed(Guid processingId, DateTime utcNow)
    {
        EnsureOwner(processingId); Status = BillingOutboxStatus.Processed; ProcessedAtUtc = utcNow; NextAttemptAtUtc = null; ClearLease(); LastError = null;
    }
    public void MarkRetry(Guid processingId, string error, DateTime retryAtUtc)
    {
        EnsureOwner(processingId); Status = BillingOutboxStatus.RetryScheduled; NextAttemptAtUtc = retryAtUtc; LastError = Truncate(error); ClearLease();
    }
    public void MarkFailed(Guid processingId, string error)
    {
        EnsureOwner(processingId); Status = BillingOutboxStatus.Failed; NextAttemptAtUtc = null; LastError = Truncate(error); ClearLease();
    }
    private void EnsureOwner(Guid processingId)
    {
        if (!IsOwnedBy(processingId)) throw new InvalidOperationException("The outbox message is not owned by this processor.");
    }
    private void ClearLease() { ProcessingId = null; LockedUntilUtc = null; }
    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}

public sealed record ClaimedBillingOutboxMessage(Guid EventId, Guid ProcessingId, string EventType, string PayloadJson);
