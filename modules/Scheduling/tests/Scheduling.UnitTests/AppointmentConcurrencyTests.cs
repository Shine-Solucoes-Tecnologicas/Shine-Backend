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

    [Fact]
    public void Appointment_keeps_customer_reference_and_contact_snapshot()
    {
        var customerId = Guid.NewGuid();
        var appointment = new Appointment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Cliente no momento da reserva", "contato antigo",
            Utc(10), Utc(11), customerId);

        Assert.Equal(customerId, appointment.CustomerId);
        Assert.Equal("Cliente no momento da reserva", appointment.CustomerName);
        Assert.Equal("contato antigo", appointment.CustomerContact);
    }

    [Fact]
    public void Appointment_rejects_empty_customer_reference()
    {
        Assert.Throws<ArgumentException>(() => new Appointment(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Cliente", "contato",
            Utc(10), Utc(11), Guid.Empty));
    }

    private static DateTime Utc(int hour) => new(2026, 8, 10, hour, 0, 0, DateTimeKind.Utc);
}
