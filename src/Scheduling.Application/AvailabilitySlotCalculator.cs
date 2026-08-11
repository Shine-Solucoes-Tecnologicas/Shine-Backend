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
        IEnumerable<(DateTime StartsAtUtc, DateTime EndsAtUtc)> occupied = null!)
    {
        var slots = calculator.Calculate(localDate, timeZoneId, durationMinutes, bufferBeforeMinutes, bufferAfterMinutes, slotIntervalMinutes,
            rules.Select(x => new Shine.Domain.AvailabilityRuleWindow(x.DayOfWeek, x.StartsAt, x.EndsAt, x.IsActive)),
            exceptions.Select(x => new Shine.Domain.AvailabilityExceptionWindow(x.Date, x.StartsAt, x.EndsAt)),
            blocks.Select(x => new Shine.Domain.AvailabilityBlockWindow(new Shine.Domain.TimeWindow(x.StartsAtUtc, x.EndsAtUtc))),
            (occupied ?? []).Select(x => new Shine.Domain.TimeWindow(x.StartsAtUtc, x.EndsAtUtc)));
        return slots.Select(x => new AvailabilitySlot(x.StartsAtUtc, x.EndsAtUtc)).ToArray();
    }
}
