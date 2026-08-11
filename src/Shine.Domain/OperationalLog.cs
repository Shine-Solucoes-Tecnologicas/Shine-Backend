namespace Shine.Domain;

public sealed class OperationalLog
{
    private OperationalLog() { }

    public Guid Id { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string Level { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public string? Exception { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? TraceId { get; private set; }

    public static OperationalLog Create(string level, string category, string message, string? exception = null, string? correlationId = null, string? traceId = null, DateTime? createdAtUtc = null) => new()
    {
        Id = Guid.NewGuid(),
        CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow,
        Level = level,
        Category = category,
        Message = message,
        Exception = exception,
        CorrelationId = correlationId,
        TraceId = traceId
    };
}
