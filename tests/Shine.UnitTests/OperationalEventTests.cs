using Shine.Domain;

namespace Shine.UnitTests;

public sealed class OperationalEventTests
{
    [Fact]
    public void Catalog_uses_stable_versioned_event_types()
    {
        var envelope = new OperationalEventEnvelope(Guid.NewGuid(), OperationalEventTypes.AppointmentCreated, 1, Guid.NewGuid(), "appointment", Guid.NewGuid(), DateTime.UtcNow, "corr-1", new Dictionary<string, string>());

        Assert.Equal("appointment.created", envelope.EventType);
        Assert.Equal(1, envelope.Version);
    }

    [Fact]
    public void Envelope_requires_utc_and_tenant_context()
    {
        Assert.Throws<ArgumentException>(() => new OperationalEventEnvelope(Guid.NewGuid(), OperationalEventTypes.AppointmentCancelled, 1, Guid.Empty, "appointment", Guid.NewGuid(), DateTime.UtcNow, "corr-1", new Dictionary<string, string>()));
        Assert.Throws<ArgumentException>(() => new OperationalEventEnvelope(Guid.NewGuid(), OperationalEventTypes.AppointmentCancelled, 1, Guid.NewGuid(), "appointment", Guid.NewGuid(), DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified), "corr-1", new Dictionary<string, string>()));
    }
}
