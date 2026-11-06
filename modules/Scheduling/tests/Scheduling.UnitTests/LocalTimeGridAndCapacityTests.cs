using Scheduling.Application;
using Scheduling.Domain;

namespace Shine.UnitTests;

public sealed class LocalTimeGridAndCapacityTests
{
    private const string NewYork = "America/New_York";

    [Fact]
    public void Day_windows_and_local_resolution_preserve_dst_semantics()
    {
        var springDate = new DateOnly(2026, 3, 8);
        var fallDate = new DateOnly(2026, 11, 1);

        Assert.Empty(LocalTimeGrid.Resolve(springDate, new TimeSpan(2, 30, 0), NewYork));
        var repeated = LocalTimeGrid.Resolve(fallDate, new TimeSpan(1, 30, 0), NewYork);
        Assert.Equal(2, repeated.Count);
        Assert.All(repeated, x => Assert.True(x.IsAmbiguousLocalTime));
        Assert.Equal(2, repeated.Select(x => x.Utc).Distinct().Count());
        Assert.Equal([TimeSpan.FromHours(-4), TimeSpan.FromHours(-5)], repeated.Select(x => x.UtcOffset).ToArray());

        var shortDay = Assert.IsType<UtcDayWindow>(LocalTimeGrid.GetDayWindow(springDate, NewYork));
        var longDay = Assert.IsType<UtcDayWindow>(LocalTimeGrid.GetDayWindow(fallDate, NewYork));
        Assert.Equal(TimeSpan.FromHours(23), shortDay.EndUtc - shortDay.StartUtc);
        Assert.Equal(TimeSpan.FromHours(25), longDay.EndUtc - longDay.StartUtc);
    }

    [Fact]
    public void Slot_generation_skips_missing_times_and_keeps_both_repeated_occurrences()
    {
        var tenantId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var springDate = new DateOnly(2026, 3, 8);
        var springRule = new AvailabilityRule(tenantId, professionalId, springDate.DayOfWeek,
            new TimeSpan(1, 0, 0), new TimeSpan(4, 0, 0));

        var spring = new AvailabilitySlotCalculator().Calculate(springDate, NewYork, 30, 0, 0, 30,
            [springRule], [], []);

        Assert.Equal(4, spring.Count);
        Assert.DoesNotContain(spring, slot => TimeZoneInfo.ConvertTimeFromUtc(slot.StartsAtUtc,
            TimeZoneInfo.FindSystemTimeZoneById(NewYork)).Hour == 2);

        var fallDate = new DateOnly(2026, 11, 1);
        var fallRule = new AvailabilityRule(tenantId, professionalId, fallDate.DayOfWeek,
            new TimeSpan(1, 0, 0), new TimeSpan(3, 0, 0));
        var fall = new AvailabilitySlotCalculator().Calculate(fallDate, NewYork, 30, 0, 0, 30,
            [fallRule], [], []);

        Assert.Equal(6, fall.Count);
        Assert.Equal(4, fall.Count(x => x.IsAmbiguousLocalTime));
        Assert.Equal(fall.Count, fall.Select(x => x.StartsAtUtc).Distinct().Count());
        Assert.True(fall.SequenceEqual(fall.OrderBy(x => x.StartsAtUtc)));
        Assert.All(fall, slot => Assert.Equal(TimeSpan.FromMinutes(30), slot.EndsAtUtc - slot.StartsAtUtc));
    }

    [Fact]
    public void Capacity_counts_partial_occupancy_by_local_grid_bucket_and_respects_borders()
    {
        var date = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        var sameBucket = CapacityBucketEvaluator.Evaluate(date.AddMinutes(8), date.AddMinutes(10), "UTC", 15,
            0, 0, 1, [new(date.AddMinutes(1), date.AddMinutes(2))]);
        Assert.True(sameBucket.Exceeded);
        Assert.Single(sameBucket.SaturatedBuckets);
        Assert.Equal(date, sameBucket.SaturatedBuckets.Single().StartUtc);
        var afterGridChange = CapacityBucketEvaluator.Evaluate(date.AddMinutes(8), date.AddMinutes(10), "UTC", 5,
            0, 0, 1, [new(date.AddMinutes(1), date.AddMinutes(2))]);
        Assert.False(afterGridChange.Exceeded);

        var adjacent = CapacityBucketEvaluator.Evaluate(date.AddMinutes(15), date.AddMinutes(30), "UTC", 15,
            0, 0, 1, [new(date, date.AddMinutes(15))]);
        Assert.False(adjacent.Exceeded);

        var bufferTouchesPreviousBucket = CapacityBucketEvaluator.Evaluate(date.AddMinutes(15), date.AddMinutes(30), "UTC", 15,
            1, 0, 1, [new(date, date.AddMinutes(14))]);
        Assert.True(bufferTouchesPreviousBucket.Exceeded);
    }

    [Fact]
    public void Repeated_local_hour_produces_independent_utc_buckets()
    {
        var occurrences = LocalTimeGrid.Resolve(new DateOnly(2026, 11, 1), new TimeSpan(1, 30, 0), NewYork).ToArray();
        var result = CapacityBucketEvaluator.Evaluate(occurrences[0].Utc, occurrences[0].Utc.AddMinutes(15), NewYork,
            15, 0, 0, 1, [new(occurrences[1].Utc, occurrences[1].Utc.AddMinutes(15))]);

        Assert.False(result.Exceeded);
    }

    [Fact]
    public void Capacity_range_from_one_to_one_hundred_is_applied_deterministically()
    {
        var start = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        for (var capacity = 1; capacity <= 100; capacity++)
        {
            var occupied = Enumerable.Range(0, capacity)
                .Select(_ => new OccupiedPeriod(start, start.AddMinutes(15))).ToArray();
            var result = CapacityBucketEvaluator.Evaluate(start, start.AddMinutes(15), "UTC", 15,
                0, 0, capacity, occupied);
            Assert.True(result.Exceeded);
            Assert.Equal(capacity + 1, result.ResultingPeakOccupancy);
        }
    }

    [Fact]
    public void Capacity_result_does_not_depend_on_appointment_order()
    {
        var start = new DateTime(2026, 9, 10, 10, 0, 0, DateTimeKind.Utc);
        var appointments = new[]
        {
            new OccupiedPeriod(start, start.AddMinutes(10)),
            new OccupiedPeriod(start.AddMinutes(5), start.AddMinutes(20))
        };

        var forward = CapacityBucketEvaluator.Evaluate(start, start.AddMinutes(30), "UTC", 15, 0, 0, 2, appointments);
        var reverse = CapacityBucketEvaluator.Evaluate(start, start.AddMinutes(30), "UTC", 15, 0, 0, 2, appointments.Reverse().ToArray());

        Assert.Equal(forward.ResultingPeakOccupancy, reverse.ResultingPeakOccupancy);
        Assert.Equal(forward.SaturatedBuckets, reverse.SaturatedBuckets);
    }
}
