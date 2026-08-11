using Shine.Domain;

namespace Scheduling.Application;

public abstract record AppointmentEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, DateTime OccurredAtUtc) : IIntegrationEvent;
public sealed record AppointmentCreatedEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime OccurredAtUtc) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc);
public sealed record AppointmentRescheduledEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, DateTime PreviousStartsAtUtc, DateTime PreviousEndsAtUtc, DateTime StartsAtUtc, DateTime EndsAtUtc, DateTime OccurredAtUtc) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc);
public sealed record AppointmentStatusChangedEvent(Guid AppointmentId, Guid TenantId, Guid ProfessionalId, Guid ServiceId, string PreviousStatus, string Status, DateTime OccurredAtUtc) : AppointmentEvent(AppointmentId, TenantId, ProfessionalId, ServiceId, OccurredAtUtc);

public interface IAppointmentEventPublisher
{
    Task PublishAsync(AppointmentEvent appointmentEvent, CancellationToken cancellationToken = default);
}
