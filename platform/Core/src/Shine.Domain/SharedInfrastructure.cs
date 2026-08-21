namespace Shine.Domain;

public readonly record struct ConcurrencyConflict(Guid ExpectedVersion, Guid CurrentVersion);

public interface IEventPublisher
{
    Task PublishAsync(IIntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

public interface IAuditWriter
{
    Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default);
}

public sealed record AuditRecord(Guid TenantId, string EntityType, Guid EntityId, string Action, Guid? UserId, DateTime OccurredAtUtc, string? CorrelationId = null);
