using Shine.Domain;

namespace Scheduling.Application;

public abstract record AppointmentEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, DateTime OccurredAtUtc, Guid? CustomerId = null) : IIntegrationEvent;
public sealed record AppointmentCreatedEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime OccurredAtUtc, Guid? CustomerId = null) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc, CustomerId);
public sealed record AppointmentRescheduledEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, DateTime PreviousStartsAtUtc, DateTime PreviousEndsAtUtc, DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime OccurredAtUtc, Guid? CustomerId = null) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc, CustomerId);
public sealed record AppointmentStatusChangedEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, string PreviousStatus, string Status, DateTime OccurredAtUtc, Guid? CustomerId = null) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc, CustomerId);

/// <summary>Requests a future reminder processor to recalculate reminders for an appointment.</summary>
public sealed record AppointmentReminderProcessingRequestedEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, DateTime StartsAtUtc, DateTime EndsAtUtc, string Trigger, DateTime OccurredAtUtc) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc);
public sealed record AppointmentReminderInvalidatedEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, string Trigger, DateTime OccurredAtUtc) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc);

public interface IAppointmentEventPublisher
{
    Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default);
}
