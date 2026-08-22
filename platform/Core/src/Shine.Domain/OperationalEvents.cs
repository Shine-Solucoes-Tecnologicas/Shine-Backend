namespace Shine.Domain;

public static class OperationalEventTypes
{
    public const string AppointmentCreated = "appointment.created";
    public const string AppointmentRescheduled = "appointment.rescheduled";
    public const string AppointmentCancelled = "appointment.cancelled";
    public const string AppointmentCompleted = "appointment.completed";
    public const string AppointmentNoShow = "appointment.no_show";
}

/// <summary>Provider-neutral envelope for operational consumers.</summary>
public sealed record OperationalEventEnvelope
{
    public OperationalEventEnvelope(Guid eventId, string eventType, int version, Guid tenantId, string entityType, Guid entityId, DateTime occurredAtUtc, string correlationId, IReadOnlyDictionary<string, string> data)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event id is required.", nameof(eventId));
        if (string.IsNullOrWhiteSpace(eventType)) throw new ArgumentException("Event type is required.", nameof(eventType));
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(entityType)) throw new ArgumentException("Entity type is required.", nameof(entityType));
        if (entityId == Guid.Empty) throw new ArgumentException("Entity id is required.", nameof(entityId));
        if (occurredAtUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Occurrence must be UTC.", nameof(occurredAtUtc));
        if (string.IsNullOrWhiteSpace(correlationId)) throw new ArgumentException("Correlation id is required.", nameof(correlationId));
        ArgumentNullException.ThrowIfNull(data);
        EventId = eventId; EventType = eventType.Trim(); Version = version; TenantId = tenantId; EntityType = entityType.Trim(); EntityId = entityId; OccurredAtUtc = occurredAtUtc; CorrelationId = correlationId.Trim(); Data = data;
    }

    public Guid EventId { get; }
    public string EventType { get; }
    public int Version { get; }
    public Guid TenantId { get; }
    public string EntityType { get; }
    public Guid EntityId { get; }
    public DateTime OccurredAtUtc { get; }
    public string CorrelationId { get; }
    public IReadOnlyDictionary<string, string> Data { get; }
}
