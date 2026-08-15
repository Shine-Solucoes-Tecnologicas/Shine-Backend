using Scheduling.Domain;

namespace Shine.UnitTests;

public sealed class AppointmentReminderTests
{
    [Fact]
    public void Preference_is_provider_neutral_and_tenant_scoped()
    {
        var preference = new AppointmentReminderPreference(Guid.NewGuid(), AppointmentReminderType.BeforeAppointment, AppointmentReminderChannel.Internal, TimeSpan.FromHours(2));

        Assert.True(preference.Enabled);
        Assert.Equal(TimeSpan.FromHours(2), preference.Offset);
    }

    [Fact]
    public void Preference_rejects_invalid_offset()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AppointmentReminderPreference(Guid.NewGuid(), AppointmentReminderType.BeforeAppointment, AppointmentReminderChannel.Email, TimeSpan.Zero));
    }

    [Fact]
    public void Request_generates_stable_idempotency_key()
    {
        var appointmentId = Guid.NewGuid();
        var scheduled = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
        var request = new AppointmentReminderRequest(Guid.NewGuid(), appointmentId, AppointmentReminderType.BeforeAppointment, AppointmentReminderChannel.Internal, scheduled);

        Assert.Contains(appointmentId.ToString("N"), request.IdempotencyKey);
        Assert.Equal(request.IdempotencyKey, new AppointmentReminderRequest(request.TenantId, appointmentId, request.Type, request.Channel, scheduled).IdempotencyKey);
    }

    [Fact]
    public void Request_requires_utc_schedule()
    {
        Assert.Throws<ArgumentException>(() => new AppointmentReminderRequest(Guid.NewGuid(), Guid.NewGuid(), AppointmentReminderType.BeforeAppointment, AppointmentReminderChannel.Internal, new DateTime(2026, 8, 14, 10, 0, 0)));
    }
}
