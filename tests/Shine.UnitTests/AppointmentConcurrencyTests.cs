using Scheduling.Domain;

namespace Shine.UnitTests;

public sealed class AppointmentConcurrencyTests
{
    [Fact]
    public void Reschedule_changes_version_and_period()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Cliente", " contato ",
            Utc(10), Utc(11));
        var originalVersion = appointment.Version;

        appointment.Reschedule(Utc(12), Utc(13));

        Assert.NotEqual(originalVersion, appointment.Version);
        Assert.Equal(Utc(12), appointment.StartsAtUtc);
        Assert.Equal(Utc(13), appointment.EndsAtUtc);
        Assert.IsAssignableFrom<Shine.Domain.IConcurrencyTracked>(appointment);
    }

    [Fact]
    public void Reschedule_rejects_terminal_appointment()
    {
        var appointment = new Appointment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Cliente", "contato",
            Utc(10), Utc(11));
        appointment.ChangeStatus(AppointmentStatus.Cancelled);

        Assert.Throws<InvalidOperationException>(() => appointment.Reschedule(Utc(12), Utc(13)));
    }

    private static DateTime Utc(int hour) => new(2026, 8, 10, hour, 0, 0, DateTimeKind.Utc);
}
