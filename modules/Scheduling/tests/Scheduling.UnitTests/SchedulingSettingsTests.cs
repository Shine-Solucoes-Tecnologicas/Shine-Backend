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
}
