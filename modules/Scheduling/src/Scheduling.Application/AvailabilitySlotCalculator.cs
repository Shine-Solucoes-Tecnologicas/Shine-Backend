using Scheduling.Domain;

namespace Scheduling.Application;

public sealed record AvailabilitySlot(DateTime StartsAtUtc, DateTime EndsAtUtc, TimeSpan UtcOffset, bool IsAmbiguousLocalTime);

public sealed class AvailabilitySlotCalculator
{
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
        if (durationMinutes <= 0 || slotIntervalMinutes <= 0 || bufferBeforeMinutes < 0 || bufferAfterMinutes < 0)
            throw new ArgumentOutOfRangeException();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var occupiedPeriods = (occupied ?? []).Select(x => new OccupiedPeriod(x.StartsAtUtc, x.EndsAtUtc)).ToArray();
        var policy = new Shine.Domain.CapacityPolicy(maxConcurrentAppointments, conflictMode);
        var activeBlocks = blocks.Where(x => x.EndsAtUtc > x.StartsAtUtc).ToArray();
        var result = new List<AvailabilitySlot>();
        foreach (var rule in rules.Where(x => x.IsActive && x.DayOfWeek == localDate.DayOfWeek).OrderBy(x => x.StartsAt))
        {
            for (var cursor = rule.StartsAt; cursor.Add(TimeSpan.FromMinutes(durationMinutes + bufferAfterMinutes)) <= rule.EndsAt;
                 cursor = cursor.Add(TimeSpan.FromMinutes(slotIntervalMinutes)))
            {
                if (cursor.Subtract(TimeSpan.FromMinutes(bufferBeforeMinutes)) < rule.StartsAt) continue;
                if (IsExcepted(localDate, cursor, durationMinutes, bufferBeforeMinutes, bufferAfterMinutes, exceptions)) continue;

                foreach (var instant in LocalTimeGrid.Resolve(localDate, cursor, timeZoneId))
                {
                    var endUtc = instant.Utc.AddMinutes(durationMinutes);
                    var bufferedStart = instant.Utc.AddMinutes(-bufferBeforeMinutes);
                    var bufferedEnd = endUtc.AddMinutes(bufferAfterMinutes);
                    var localBufferedStart = TimeZoneInfo.ConvertTimeFromUtc(bufferedStart, zone);
                    var localBufferedEnd = TimeZoneInfo.ConvertTimeFromUtc(bufferedEnd, zone);
                    if (DateOnly.FromDateTime(localBufferedStart) != localDate || DateOnly.FromDateTime(localBufferedEnd) != localDate ||
                        localBufferedStart.TimeOfDay < rule.StartsAt || localBufferedEnd.TimeOfDay > rule.EndsAt) continue;
                    if (activeBlocks.Any(block => Intersects(bufferedStart, bufferedEnd, block.StartsAtUtc, block.EndsAtUtc))) continue;

                    var directConflicts = occupiedPeriods.Count(x => Intersects(bufferedStart, bufferedEnd,
                        x.StartUtc.AddMinutes(-bufferBeforeMinutes), x.EndUtc.AddMinutes(bufferAfterMinutes)));
                    var capacity = CapacityBucketEvaluator.Evaluate(instant.Utc, endUtc, timeZoneId, slotIntervalMinutes,
                        bufferBeforeMinutes, bufferAfterMinutes, maxConcurrentAppointments, occupiedPeriods);
                    if (capacity.Exceeded || policy.ConflictMode == Shine.Domain.ConflictMode.Block && directConflicts > 0) continue;
                    result.Add(new(instant.Utc, endUtc, instant.UtcOffset, instant.IsAmbiguousLocalTime));
                }
            }
        }

        return result.DistinctBy(x => x.StartsAtUtc).OrderBy(x => x.StartsAtUtc).ToArray();
    }

    private static bool IsExcepted(DateOnly date, TimeSpan start, int durationMinutes, int bufferBeforeMinutes,
        int bufferAfterMinutes, IEnumerable<AvailabilityException> exceptions)
    {
        var candidateStart = start.Subtract(TimeSpan.FromMinutes(bufferBeforeMinutes));
        var candidateEnd = start.Add(TimeSpan.FromMinutes(durationMinutes + bufferAfterMinutes));
        return exceptions.Any(exception => exception.Date == date &&
            (exception.StartsAt is null || exception.StartsAt < candidateEnd && exception.EndsAt > candidateStart));
    }

    private static bool Intersects(DateTime leftStart, DateTime leftEnd, DateTime rightStart, DateTime rightEnd) =>
        leftStart < rightEnd && leftEnd > rightStart;
}
