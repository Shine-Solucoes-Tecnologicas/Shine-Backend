using Scheduling.Domain;
using Shine.Domain;

namespace Shine.UnitTests;

public sealed class SchedulingSettingsTests
{
    [Fact]
    public void Defaults_are_safe_for_a_new_tenant()
    {
        var settings = new SchedulingSettings(Guid.NewGuid());

        Assert.Equal(15, settings.SlotIntervalMinutes);
        Assert.Equal(ConflictMode.WarnAndConfirm, settings.ConflictMode);
        Assert.Equal(1, settings.DefaultMaxConcurrentAppointments);
    }

    [Fact]
    public void Update_persists_operational_policy_values()
    {
        var settings = new SchedulingSettings(Guid.NewGuid());

        settings.Update(30, 10, 5, "UTC", ConflictMode.Allow, 4);

        Assert.Equal(30, settings.SlotIntervalMinutes);
        Assert.Equal(10, settings.BufferBeforeMinutes);
        Assert.Equal(5, settings.BufferAfterMinutes);
        Assert.Equal("UTC", settings.TimeZoneId);
        Assert.Equal(ConflictMode.Allow, settings.ConflictMode);
        Assert.Equal(4, settings.DefaultMaxConcurrentAppointments);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(121, 1)]
    [InlineData(15, 0)]
    [InlineData(15, 101)]
    public void Update_rejects_invalid_operational_values(int interval, int capacity)
    {
        var settings = new SchedulingSettings(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => settings.Update(interval, 0, 0, "UTC", ConflictMode.WarnAndConfirm, capacity));
    }

    [Fact]
    public void Update_rejects_unknown_conflict_mode()
    {
        var settings = new SchedulingSettings(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => settings.Update(15, 0, 0, "UTC", (ConflictMode)99, 1));
    }

    [Fact]
    public void Update_rejects_unknown_time_zone()
    {
        var settings = new SchedulingSettings(Guid.NewGuid());

        Assert.Throws<ArgumentException>(() => settings.Update(15, 0, 0, "Invalid/Zone", ConflictMode.WarnAndConfirm, 1));
    }

    [Fact]
    public void Professional_capacity_is_a_scheduling_concern()
    {
        var settings = new ProfessionalSchedulingSettings(Guid.NewGuid(), Guid.NewGuid(), 2);
        settings.SetCapacity(4);
        Assert.Equal(4, settings.MaxConcurrentAppointments);
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.SetCapacity(0));
    }

    [Fact]
    public void Variable_duration_and_professional_override_are_scheduling_concerns()
    {
        var tenantId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var serviceId = Guid.NewGuid();
        var service = new ServiceSchedulingSettings(tenantId, serviceId);
        service.ConfigureVariableDuration("hair-length", 15, 30, 120, "v1");
        var association = new ProfessionalServiceSchedulingSettings(tenantId, professionalId, serviceId, 45);

        Assert.Equal("hair-length", service.DurationAttributeKey);
        Assert.Equal(45, association.DurationOverrideMinutes);
    }
}
