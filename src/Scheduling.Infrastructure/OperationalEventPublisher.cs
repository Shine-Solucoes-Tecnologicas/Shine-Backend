using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;

namespace Scheduling.Infrastructure;

public sealed class OperationalEventPublisher(SchedulingDbContext db)
{
    public async Task<bool> PublishAsync(OperationalEventEnvelope operationalEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operationalEvent);
        var key = $"operational:{operationalEvent.EventId:N}";
        if (await db.OutboxMessages.AnyAsync(x => x.IdempotencyKey == key, cancellationToken)) return false;

        db.OutboxMessages.Add(new OutboxMessage(operationalEvent.EventType, JsonSerializer.Serialize(operationalEvent), operationalEvent.OccurredAtUtc, key));
        return true;
    }
}
