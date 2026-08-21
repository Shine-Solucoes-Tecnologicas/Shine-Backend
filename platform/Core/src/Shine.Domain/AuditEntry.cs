namespace Shine.Domain;

public sealed class AuditEntry
{
    private AuditEntry() { }

    public Guid Id { get; private set; }
    public string EntityType { get; private set; } = string.Empty;
    public string EntityId { get; private set; } = string.Empty;
    public string Action { get; private set; } = string.Empty;
    public Guid? UserId { get; private set; }
    public Guid? TenantId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? OldValuesJson { get; private set; }
    public string? NewValuesJson { get; private set; }
    public bool IsSystemOperation { get; private set; }

    public static AuditEntry Create(
        string entityType,
        string entityId,
        string action,
        Guid? userId,
        Guid? tenantId,
        DateTime occurredAtUtc,
        string? correlationId = null,
        string? ipAddress = null,
        string? userAgent = null,
        string? oldValuesJson = null,
        string? newValuesJson = null) => new()
        {
            Id = Guid.NewGuid(),
            EntityType = Require(entityType, nameof(entityType)),
            EntityId = Require(entityId, nameof(entityId)),
            Action = Require(action, nameof(action)),
            UserId = userId,
            TenantId = tenantId,
            OccurredAtUtc = occurredAtUtc.Kind == DateTimeKind.Utc ? occurredAtUtc : occurredAtUtc.ToUniversalTime(),
            CorrelationId = correlationId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            OldValuesJson = oldValuesJson,
            NewValuesJson = newValuesJson,
            IsSystemOperation = userId is null
        };

    private static string Require(string value, string name) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
