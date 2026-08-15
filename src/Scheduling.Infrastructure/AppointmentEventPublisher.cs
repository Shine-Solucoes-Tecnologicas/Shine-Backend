using System.Text.Json;
using Scheduling.Application;

namespace Scheduling.Infrastructure;

public sealed class AppointmentEventPublisher(SchedulingDbContext db) : IAppointmentEventPublisher
{
    public Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default)
    {
        db.OutboxMessages.Add(new OutboxMessage(appointmentEvent.GetType().Name, JsonSerializer.Serialize(appointmentEvent, appointmentEvent.GetType()), appointmentEvent.OccurredAtUtc));
        if (appointmentEvent is AppointmentCreatedEvent created)
            AddReminderRequest(created.AppointmentId, created.TenantId, created.ProfessionalId, created.ServiceId, created.StartsAtUtc, created.EndsAtUtc, "created", created.OccurredAtUtc);
        else if (appointmentEvent is AppointmentRescheduledEvent rescheduled)
            AddReminderRequest(rescheduled.AppointmentId, rescheduled.TenantId, rescheduled.ProfessionalId, rescheduled.ServiceId, rescheduled.StartsAtUtc, rescheduled.EndsAtUtc, "rescheduled", rescheduled.OccurredAtUtc);
        return Task.CompletedTask;
    }

    private void AddReminderRequest(Guid appointmentId, Guid tenantId, Guid professionalId, Guid serviceId, DateTime startsAtUtc, DateTime endsAtUtc, string trigger, DateTime occurredAtUtc)
    {
        var reminder = new AppointmentReminderProcessingRequestedEvent(appointmentId, tenantId, professionalId, serviceId, startsAtUtc, endsAtUtc, trigger, occurredAtUtc);
        db.OutboxMessages.Add(new OutboxMessage(reminder.GetType().Name, JsonSerializer.Serialize(reminder), occurredAtUtc));
    }
}
