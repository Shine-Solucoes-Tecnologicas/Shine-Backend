using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Scheduling.Application;
using Shine.Domain;

namespace Scheduling.Infrastructure;

public sealed class AppointmentEventPublisher(SchedulingDbContext db) : IAppointmentEventPublisher
{
    public async Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default)
    {
        var eventKey = EventKey(appointmentEvent);
        await AddOnceAsync(new OutboxMessage(appointmentEvent.GetType().Name, JsonSerializer.Serialize(appointmentEvent, appointmentEvent.GetType()), appointmentEvent.OccurredAtUtc, eventKey), cancellationToken);
        if (appointmentEvent is AppointmentCreatedEvent created)
            await AddReminderRequestAsync(created.AppointmentId, created.TenantId, created.ProfessionalId, created.ServiceId, created.StartsAtUtc, created.EndsAtUtc, "created", created.OccurredAtUtc, cancellationToken);
        else if (appointmentEvent is AppointmentRescheduledEvent rescheduled)
            await AddReminderRequestAsync(rescheduled.AppointmentId, rescheduled.TenantId, rescheduled.ProfessionalId, rescheduled.ServiceId, rescheduled.StartsAtUtc, rescheduled.EndsAtUtc, "rescheduled", rescheduled.OccurredAtUtc, cancellationToken);
        else if (appointmentEvent is AppointmentStatusChangedEvent status && string.Equals(status.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
            await AddOnceAsync(new OutboxMessage(nameof(AppointmentReminderInvalidatedEvent), JsonSerializer.Serialize(
                new AppointmentReminderInvalidatedEvent(status.AppointmentId, status.TenantId, status.ProfessionalId, status.ServiceId, "cancelled", status.OccurredAtUtc)), status.OccurredAtUtc,
                $"appointment:{status.AppointmentId:N}:reminder:invalidate:cancelled"), cancellationToken);

        await AddOperationalEventAsync(appointmentEvent, eventKey, cancellationToken);
    }

    private Task AddReminderRequestAsync(Guid appointmentId, Guid tenantId, Guid professionalId, Guid serviceId, DateTime startsAtUtc, DateTime endsAtUtc, string trigger, DateTime occurredAtUtc, CancellationToken cancellationToken)
    {
        var reminder = new AppointmentReminderProcessingRequestedEvent(appointmentId, tenantId, professionalId, serviceId, startsAtUtc, endsAtUtc, trigger, occurredAtUtc);
        return AddOnceAsync(new OutboxMessage(reminder.GetType().Name, JsonSerializer.Serialize(reminder), occurredAtUtc,
            $"appointment:{appointmentId:N}:reminder:{trigger}:{startsAtUtc.Ticks}"), cancellationToken);
    }

    private async Task AddOperationalEventAsync(AppointmentEvent appointmentEvent, string eventKey, CancellationToken cancellationToken)
    {
        var eventType = appointmentEvent switch
        {
            AppointmentCreatedEvent => OperationalEventTypes.AppointmentCreated,
            AppointmentRescheduledEvent => OperationalEventTypes.AppointmentRescheduled,
            AppointmentStatusChangedEvent status when string.Equals(status.Status, "Cancelled", StringComparison.OrdinalIgnoreCase) => OperationalEventTypes.AppointmentCancelled,
            AppointmentStatusChangedEvent status when string.Equals(status.Status, "Completed", StringComparison.OrdinalIgnoreCase) => OperationalEventTypes.AppointmentCompleted,
            AppointmentStatusChangedEvent status when string.Equals(status.Status, "NoShow", StringComparison.OrdinalIgnoreCase) => OperationalEventTypes.AppointmentNoShow,
            _ => null
        };
        if (eventType is null) return;

        var envelope = new OperationalEventEnvelope(DeterministicGuid(eventKey), eventType, 1, appointmentEvent.TenantId,
            "appointment", appointmentEvent.AppointmentId, appointmentEvent.OccurredAtUtc, eventKey,
            new Dictionary<string, string> { ["professionalId"] = appointmentEvent.ProfessionalId.ToString("N"), ["serviceId"] = appointmentEvent.ServiceId.ToString("N") });
        await AddOnceAsync(new OutboxMessage(eventType, JsonSerializer.Serialize(envelope), appointmentEvent.OccurredAtUtc,
            $"operational:{envelope.EventId:N}"), cancellationToken);
    }

    private async Task AddOnceAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        if (message.IdempotencyKey is not null && (db.OutboxMessages.Local.Any(x => x.IdempotencyKey == message.IdempotencyKey) ||
            await db.OutboxMessages.AnyAsync(x => x.IdempotencyKey == message.IdempotencyKey, cancellationToken))) return;
        db.OutboxMessages.Add(message);
    }

    private static string EventKey(AppointmentEvent item) => item switch
    {
        AppointmentCreatedEvent => $"appointment:{item.AppointmentId:N}:created",
        AppointmentRescheduledEvent changed => $"appointment:{item.AppointmentId:N}:rescheduled:{changed.StartsAtUtc.Ticks}",
        AppointmentStatusChangedEvent changed => $"appointment:{item.AppointmentId:N}:status:{changed.Status.ToLowerInvariant()}",
        _ => $"appointment:{item.AppointmentId:N}:{item.GetType().Name}:{item.OccurredAtUtc.Ticks}"
    };

    private static Guid DeterministicGuid(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
