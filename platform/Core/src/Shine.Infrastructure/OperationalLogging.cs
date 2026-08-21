using Shine.Domain;
using Shine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;

namespace Shine.Infrastructure;

public interface IOperationalLogWriter
{
    Task WriteAsync(string level, string category, string message, Exception? exception = null, CancellationToken cancellationToken = default);
}

public sealed class OperationalLogWriter(ShineDbContext db, IHttpContextAccessor httpContextAccessor) : IOperationalLogWriter
{
    public async Task WriteAsync(string level, string category, string message, Exception? exception = null, CancellationToken cancellationToken = default)
    {
        var context = httpContextAccessor.HttpContext;
        var correlationId = context?.Items["X-Correlation-ID"]?.ToString();
        var traceId = context?.TraceIdentifier;
        db.OperationalLogs.Add(OperationalLog.Create(level, category, message, exception?.ToString(), correlationId, traceId));
        await db.SaveChangesAsync(cancellationToken);
    }
}
