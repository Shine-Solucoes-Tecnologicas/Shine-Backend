using Scheduling.Application;
using Scheduling.Domain;

namespace Shine.UnitTests;

public sealed class AvailabilitySlotCalculatorTests
{
    [Fact]
    public void Generates_slots_with_interval_and_service_duration()
    {
        var date = new DateOnly(2026, 8, 10);
        var professionalId = Guid.NewGuid();
        var rule = new AvailabilityRule(Guid.NewGuid(), professionalId, date.DayOfWeek, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0));
        var slots = new AvailabilitySlotCalculator().Calculate(date, "UTC", 30, 0, 0, 30, [rule], [], []);
        Assert.Equal(2, slots.Count);
        Assert.Equal(new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc), slots.First().StartsAtUtc);
    }

    [Fact]
    public void Excludes_slots_intersecting_blocks_and_exceptions()
    {
        var date = new DateOnly(2026, 8, 10);
        var tenantId = Guid.NewGuid(); var professionalId = Guid.NewGuid();
        var rule = new AvailabilityRule(tenantId, professionalId, date.DayOfWeek, new TimeSpan(9, 0, 0), new TimeSpan(12, 0, 0));
        var block = new ScheduleBlock(tenantId, professionalId, new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc), new DateTime(2026, 8, 10, 11, 0, 0, DateTimeKind.Utc), "Bloqueio");
        var exception = new AvailabilityException(tenantId, professionalId, date, new TimeSpan(9, 0, 0), new TimeSpan(10, 0, 0), "Indisponível");
        var slots = new AvailabilitySlotCalculator().Calculate(date, "UTC", 30, 0, 0, 30, [rule], [exception], [block]);
        Assert.Equal(2, slots.Count);
        Assert.All(slots, slot => Assert.True(slot.StartsAtUtc.Hour >= 11));
    }

    [Fact]
    public void Excludes_slots_intersecting_occupied_appointments()
    {
        var date = new DateOnly(2026, 8, 10);
        var professionalId = Guid.NewGuid();
        var rule = new AvailabilityRule(Guid.NewGuid(), professionalId, date.DayOfWeek, new TimeSpan(9, 0, 0), new TimeSpan(10, 30, 0));
        var occupied = new[] { (StartsAtUtc: new DateTime(2026, 8, 10, 9, 0, 0, DateTimeKind.Utc), EndsAtUtc: new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc)) };

        var slots = new AvailabilitySlotCalculator().Calculate(date, "UTC", 30, 0, 0, 30, [rule], [], [], occupied);

        Assert.Single(slots);
        Assert.Equal(new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc), slots.Single().StartsAtUtc);
    }
}
