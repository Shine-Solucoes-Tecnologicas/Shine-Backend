namespace Scheduling.Domain;

public sealed class Professional
{
    private Professional() { }
    public Professional(Guid tenantId, string name, Guid? userId = null, int maxConcurrentAppointments = 1) { TenantId = tenantId; Name = Required(name, nameof(name)); UserId = userId; SetMaxConcurrentAppointments(maxConcurrentAppointments); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; } = true;
    public int MaxConcurrentAppointments { get; private set; } = 1;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public void Rename(string name) => Name = Required(name, nameof(name));
    public void SetActive(bool active) => IsActive = active;
    public void SetMaxConcurrentAppointments(int value) { if (value <= 0 || value > 100) throw new ArgumentOutOfRangeException(nameof(value)); MaxConcurrentAppointments = value; }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class Service
{
    private Service() { }
    public Service(Guid tenantId, string name, int durationMinutes) { TenantId = tenantId; Name = Required(name, nameof(name)); SetDuration(durationMinutes); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public int DurationMinutes { get; private set; }
    public string? DurationAttributeKey { get; private set; }
    public int? MinutesPerAttributeUnit { get; private set; }
    public int? MinimumDurationMinutes { get; private set; }
    public int? MaximumDurationMinutes { get; private set; }
    public string? DurationRuleVersion { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public void Rename(string name) => Name = Required(name, nameof(name));
    public void SetDuration(int durationMinutes) { if (durationMinutes <= 0 || durationMinutes > 1440) throw new ArgumentOutOfRangeException(nameof(durationMinutes)); DurationMinutes = durationMinutes; }
    public void ConfigureVariableDuration(string attributeKey, int minutesPerUnit, int minimumMinutes, int maximumMinutes, string version)
    {
        var policy = new ServiceDurationPolicy(attributeKey.Trim(), minutesPerUnit, minimumMinutes, maximumMinutes, version.Trim()).Validate();
        DurationAttributeKey = policy.AttributeKey; MinutesPerAttributeUnit = policy.MinutesPerUnit; MinimumDurationMinutes = policy.MinimumMinutes; MaximumDurationMinutes = policy.MaximumMinutes; DurationRuleVersion = policy.Version;
    }
    public void UseFixedDuration() { DurationAttributeKey = null; MinutesPerAttributeUnit = null; MinimumDurationMinutes = null; MaximumDurationMinutes = null; DurationRuleVersion = null; }
    public ServiceDurationPolicy? GetDurationPolicy() => DurationAttributeKey is null ? null : new ServiceDurationPolicy(DurationAttributeKey, MinutesPerAttributeUnit!.Value, MinimumDurationMinutes!.Value, MaximumDurationMinutes!.Value, DurationRuleVersion!).Validate();
    public void SetActive(bool active) => IsActive = active;
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}

public sealed class ProfessionalService
{
    private ProfessionalService() { }
    public ProfessionalService(Guid tenantId, Guid professionalId, Guid serviceId, int? durationOverrideMinutes = null) { TenantId = tenantId; ProfessionalId = professionalId; ServiceId = serviceId; SetDurationOverride(durationOverrideMinutes); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }
    public int? DurationOverrideMinutes { get; private set; }
    public bool IsActive { get; private set; } = true;
    public void SetDurationOverride(int? value) { if (value is <= 0 or > 1440) throw new ArgumentOutOfRangeException(nameof(value)); DurationOverrideMinutes = value; }
    public void SetActive(bool active) => IsActive = active;
}

public sealed class AvailabilityRule
{
    private AvailabilityRule() { }
    public AvailabilityRule(Guid tenantId, Guid professionalId, DayOfWeek dayOfWeek, TimeSpan startsAt, TimeSpan endsAt) { TenantId = tenantId; ProfessionalId = professionalId; DayOfWeek = dayOfWeek; SetPeriod(startsAt, endsAt); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeSpan StartsAt { get; private set; }
    public TimeSpan EndsAt { get; private set; }
    public bool IsActive { get; private set; } = true;
    public void SetPeriod(TimeSpan startsAt, TimeSpan endsAt) { if (startsAt < TimeSpan.Zero || endsAt > TimeSpan.FromDays(1) || startsAt >= endsAt) throw new ArgumentException("Availability period is invalid."); StartsAt = startsAt; EndsAt = endsAt; }
    public void SetActive(bool active) => IsActive = active;
}

public sealed class AvailabilityException
{
    private AvailabilityException() { }
    public AvailabilityException(Guid tenantId, Guid professionalId, DateOnly date, TimeSpan? startsAt, TimeSpan? endsAt, string reason) { TenantId = tenantId; ProfessionalId = professionalId; Date = date; Reason = Required(reason); if (startsAt is null != (endsAt is null)) throw new ArgumentException("Exception period must include both start and end."); if (startsAt is not null) SetPeriod(startsAt.Value, endsAt!.Value); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public DateOnly Date { get; private set; }
    public TimeSpan? StartsAt { get; private set; }
    public TimeSpan? EndsAt { get; private set; }
    public string Reason { get; private set; } = null!;
    public void SetPeriod(TimeSpan startsAt, TimeSpan endsAt) { if (startsAt < TimeSpan.Zero || endsAt > TimeSpan.FromDays(1) || startsAt >= endsAt) throw new ArgumentException("Exception period is invalid."); StartsAt = startsAt; EndsAt = endsAt; }
    private static string Required(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Reason is required.") : value.Trim();
}

public sealed class ScheduleBlock
{
    private ScheduleBlock() { }
    public ScheduleBlock(Guid tenantId, Guid professionalId, DateTime startsAtUtc, DateTime endsAtUtc, string reason) { TenantId = tenantId; ProfessionalId = professionalId; SetPeriod(startsAtUtc, endsAtUtc); Reason = string.IsNullOrWhiteSpace(reason) ? throw new ArgumentException("Reason is required.") : reason.Trim(); }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public string Reason { get; private set; } = null!;
    public void SetPeriod(DateTime startsAtUtc, DateTime endsAtUtc) { if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc || startsAtUtc >= endsAtUtc) throw new ArgumentException("Block period is invalid."); StartsAtUtc = startsAtUtc; EndsAtUtc = endsAtUtc; }
}

public sealed class SchedulingSettings
{
    private SchedulingSettings() { }
    public SchedulingSettings(Guid tenantId) { TenantId = tenantId; }
    public Guid TenantId { get; private set; }
    public int SlotIntervalMinutes { get; private set; } = 15;
    public int BufferBeforeMinutes { get; private set; }
    public int BufferAfterMinutes { get; private set; }
    public string TimeZoneId { get; private set; } = "America/Sao_Paulo";
    public Shine.Domain.ConflictMode ConflictMode { get; private set; } = Shine.Domain.ConflictMode.WarnAndConfirm;
    public int DefaultMaxConcurrentAppointments { get; private set; } = 1;
    public void Update(int slotIntervalMinutes, int bufferBeforeMinutes, int bufferAfterMinutes, string timeZoneId, Shine.Domain.ConflictMode conflictMode = Shine.Domain.ConflictMode.WarnAndConfirm, int defaultMaxConcurrentAppointments = 1) { var normalizedTimeZoneId = timeZoneId?.Trim(); if (slotIntervalMinutes <= 0 || slotIntervalMinutes > 120 || bufferBeforeMinutes < 0 || bufferAfterMinutes < 0 || string.IsNullOrWhiteSpace(normalizedTimeZoneId) || !Enum.IsDefined(conflictMode) || defaultMaxConcurrentAppointments < 1 || defaultMaxConcurrentAppointments > 100) throw new ArgumentException("Scheduling settings are invalid."); try { TimeZoneInfo.FindSystemTimeZoneById(normalizedTimeZoneId); } catch (TimeZoneNotFoundException) { throw new ArgumentException("Scheduling time zone is invalid.", nameof(timeZoneId)); } catch (InvalidTimeZoneException) { throw new ArgumentException("Scheduling time zone is invalid.", nameof(timeZoneId)); } SlotIntervalMinutes = slotIntervalMinutes; BufferBeforeMinutes = bufferBeforeMinutes; BufferAfterMinutes = bufferAfterMinutes; TimeZoneId = normalizedTimeZoneId; ConflictMode = conflictMode; DefaultMaxConcurrentAppointments = defaultMaxConcurrentAppointments; }
}

public enum AppointmentStatus { Scheduled, Confirmed, Completed, Cancelled, NoShow }

public sealed class Appointment : Shine.Domain.IConcurrencyTracked
{
    private Appointment() { }
    public Appointment(Guid tenantId, Guid professionalId, Guid serviceId, string customerName, string customerContact, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        TenantId = tenantId; ProfessionalId = professionalId; ServiceId = serviceId; CustomerName = Required(customerName, nameof(customerName)); CustomerContact = Required(customerContact, nameof(customerContact)); SetPeriod(startsAtUtc, endsAtUtc);
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid ProfessionalId { get; private set; }
    public Guid ServiceId { get; private set; }
    public string CustomerName { get; private set; } = null!;
    public string CustomerContact { get; private set; } = null!;
    public DateTime StartsAtUtc { get; private set; }
    public DateTime EndsAtUtc { get; private set; }
    public AppointmentStatus Status { get; private set; } = AppointmentStatus.Scheduled;
    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;
    public Guid Version { get; private set; } = Guid.NewGuid();
    public void Reschedule(DateTime startsAtUtc, DateTime endsAtUtc) { if (Status is AppointmentStatus.Cancelled or AppointmentStatus.Completed or AppointmentStatus.NoShow) throw new InvalidOperationException("Appointment cannot be rescheduled in its current state."); SetPeriod(startsAtUtc, endsAtUtc); Version = Guid.NewGuid(); }
    public void ChangeStatus(AppointmentStatus status)
    {
        var valid = (Status, status) switch
        {
            (AppointmentStatus.Scheduled, AppointmentStatus.Confirmed or AppointmentStatus.Cancelled or AppointmentStatus.NoShow) => true,
            (AppointmentStatus.Confirmed, AppointmentStatus.Completed or AppointmentStatus.Cancelled or AppointmentStatus.NoShow) => true,
            (AppointmentStatus.Cancelled, AppointmentStatus.Scheduled) => true,
            _ => false
        };
        if (!valid) throw new InvalidOperationException("Invalid appointment transition.");
        Status = status;
    }
    private void SetPeriod(DateTime startsAtUtc, DateTime endsAtUtc) { if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc || startsAtUtc >= endsAtUtc) throw new ArgumentException("Appointment period is invalid."); StartsAtUtc = startsAtUtc; EndsAtUtc = endsAtUtc; }
    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
