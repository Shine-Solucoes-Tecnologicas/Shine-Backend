namespace Scheduling.Domain;

public enum AppointmentReminderChannel { Internal, Email, WhatsApp }

public enum AppointmentReminderType { BeforeAppointment }

/// <summary>Tenant-owned reminder preference, independent from any delivery provider.</summary>
public sealed record AppointmentReminderPreference
{
    public AppointmentReminderPreference(Guid tenantId, AppointmentReminderType type, AppointmentReminderChannel channel, TimeSpan offset, bool enabled = true)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (offset <= TimeSpan.Zero || offset > TimeSpan.FromDays(30)) throw new ArgumentOutOfRangeException(nameof(offset));
        TenantId = tenantId;
        Type = type;
        Channel = channel;
        Offset = offset;
        Enabled = enabled;
    }

    public Guid TenantId { get; }
    public AppointmentReminderType Type { get; }
    public AppointmentReminderChannel Channel { get; }
    public TimeSpan Offset { get; }
    public bool Enabled { get; }
}

/// <summary>Provider-neutral reminder request with a stable idempotency key.</summary>
public sealed record AppointmentReminderRequest
{
    public AppointmentReminderRequest(Guid tenantId, Guid appointmentId, AppointmentReminderType type, AppointmentReminderChannel channel, DateTime scheduledForUtc)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (appointmentId == Guid.Empty) throw new ArgumentException("Appointment is required.", nameof(appointmentId));
        if (scheduledForUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Scheduled time must be UTC.", nameof(scheduledForUtc));
        TenantId = tenantId;
        AppointmentId = appointmentId;
        Type = type;
        Channel = channel;
        ScheduledForUtc = scheduledForUtc;
        IdempotencyKey = $"appointment:{appointmentId:N}:reminder:{type}:{channel}:{scheduledForUtc.Ticks}";
    }

    public Guid TenantId { get; }
    public Guid AppointmentId { get; }
    public AppointmentReminderType Type { get; }
    public AppointmentReminderChannel Channel { get; }
    public DateTime ScheduledForUtc { get; }
    public string IdempotencyKey { get; }
}
