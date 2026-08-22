using Scheduling.Domain;

namespace Scheduling.Application;

public sealed record AvailabilitySlot(DateTime StartsAtUtc, DateTime EndsAtUtc);

public sealed class AvailabilitySlotCalculator
{
    private readonly Shine.Domain.AvailabilityCalculator calculator = new();
    public IReadOnlyCollection<AvailabilitySlot> Calculate(
        DateOnly localDate,
        string timeZoneId,
        int durationMinutes,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        int slotIntervalMinutes,
        IEnumerable<AvailabilityRule> rules,
        IEnumerable<AvailabilityException> exceptions,
        IEnumerable<ScheduleBlock> blocks,
        IEnumerable<(DateTime StartsAtUtc, DateTime EndsAtUtc)> occupied = null!,
        int maxConcurrentAppointments = 1,
        Shine.Domain.ConflictMode conflictMode = Shine.Domain.ConflictMode.WarnAndConfirm)
    {
        var occupiedWindows = (occupied ?? []).Select(x => new Shine.Domain.TimeWindow(x.StartsAtUtc, x.EndsAtUtc)).ToArray();
        var policy = new Shine.Domain.CapacityPolicy(maxConcurrentAppointments, conflictMode);
        var slots = calculator.Calculate(localDate, timeZoneId, durationMinutes, bufferBeforeMinutes, bufferAfterMinutes, slotIntervalMinutes,
            rules.Select(x => new Shine.Domain.AvailabilityRuleWindow(x.DayOfWeek, x.StartsAt, x.EndsAt, x.IsActive)),
            exceptions.Select(x => new Shine.Domain.AvailabilityExceptionWindow(x.Date, x.StartsAt, x.EndsAt)),
            blocks.Select(x => new Shine.Domain.AvailabilityBlockWindow(new Shine.Domain.TimeWindow(x.StartsAtUtc, x.EndsAtUtc))),
            occupied: []);
        return slots.Where(slot =>
        {
            var buffered = new Shine.Domain.TimeWindow(slot.StartsAtUtc.AddMinutes(-bufferBeforeMinutes), slot.EndsAtUtc.AddMinutes(bufferAfterMinutes));
            return !policy.Blocks(occupiedWindows.Count(buffered.Overlaps));
        }).Select(x => new AvailabilitySlot(x.StartsAtUtc, x.EndsAtUtc)).ToArray();
    }
}
