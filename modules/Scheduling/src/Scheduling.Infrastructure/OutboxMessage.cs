namespace Scheduling.Infrastructure;

public sealed class OutboxMessage
{
    private OutboxMessage() { }
    public OutboxMessage(string eventType, string payload, DateTime occurredAtUtc, string? idempotencyKey = null) { Id = Guid.NewGuid(); EventType = eventType; Payload = payload; OccurredAtUtc = occurredAtUtc; IdempotencyKey = idempotencyKey; }
    public Guid Id { get; private set; }
    public string EventType { get; private set; } = null!;
    public string Payload { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public void MarkAttempt(DateTime lockedUntilUtc) { Attempts++; LockedUntilUtc = lockedUntilUtc; }
    public void MarkProcessed(DateTime processedAtUtc) { ProcessedAtUtc = processedAtUtc; LockedUntilUtc = null; }
}
