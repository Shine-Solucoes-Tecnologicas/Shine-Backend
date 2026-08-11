using System.Text.Json;
using Scheduling.Application;

namespace Scheduling.Infrastructure;

public sealed class AppointmentEventPublisher(SchedulingDbContext db) : IAppointmentEventPublisher
{
    public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default)
    {
        db.OutboxMessages.Add(new OutboxMessage(appointmentEvent.GetType().Name, JsonSerializer.Serialize(appointmentEvent, appointmentEvent.GetType()), appointmentEvent.OccurredAtUtc));
        return Task.CompletedTask;
    }
}
