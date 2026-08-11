namespace Shine.Domain;

public sealed record AvailabilityRuleWindow(DayOfWeek DayOfWeek, TimeSpan StartsAt, TimeSpan EndsAt, bool IsActive = true);
public sealed record AvailabilityExceptionWindow(DateOnly Date, TimeSpan? StartsAt, TimeSpan? EndsAt, bool IsActive = true);
public sealed record AvailabilityBlockWindow(TimeWindow Window, bool IsActive = true);
public sealed record AvailabilitySlot(DateTime StartsAtUtc, DateTime EndsAtUtc);

public sealed class AvailabilityCalculator
{
    public IReadOnlyCollection<AvailabilitySlot> Calculate(DateOnly localDate, string timeZoneId, int durationMinutes, int bufferBeforeMinutes, int bufferAfterMinutes, int slotIntervalMinutes, IEnumerable<AvailabilityRuleWindow> rules, IEnumerable<AvailabilityExceptionWindow> exceptions, IEnumerable<AvailabilityBlockWindow> blocks, IEnumerable<TimeWindow>? occupied = null)
    {
        if (durationMinutes <= 0 || slotIntervalMinutes <= 0 || bufferBeforeMinutes < 0 || bufferAfterMinutes < 0) throw new ArgumentOutOfRangeException();
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var occupiedWindows = (occupied ?? []).Concat(blocks.Where(x => x.IsActive).Select(x => x.Window)).ToArray();
        var result = new List<AvailabilitySlot>();
        foreach (var rule in rules.Where(x => x.IsActive && x.DayOfWeek == localDate.DayOfWeek).OrderBy(x => x.StartsAt))
        {
            var cursor = rule.StartsAt;
            var required = TimeSpan.FromMinutes(durationMinutes + bufferBeforeMinutes + bufferAfterMinutes);
            while (cursor + required <= rule.EndsAt)
            {
                var localStart = localDate.ToDateTime(TimeOnly.FromTimeSpan(cursor), DateTimeKind.Unspecified);
                var localEnd = localStart.AddMinutes(durationMinutes);
                var bufferedStart = localStart.AddMinutes(-bufferBeforeMinutes);
                var bufferedEnd = localEnd.AddMinutes(bufferAfterMinutes);
                var exception = exceptions.Any(x => x.IsActive && x.Date == localDate && (x.StartsAt is null || x.StartsAt <= cursor && x.EndsAt >= cursor + required));
                if (!exception)
                {
                    var window = new TimeWindow(TimeZoneInfo.ConvertTimeToUtc(bufferedStart, zone), TimeZoneInfo.ConvertTimeToUtc(bufferedEnd, zone));
                    if (!occupiedWindows.Any(window.Overlaps)) result.Add(new AvailabilitySlot(TimeZoneInfo.ConvertTimeToUtc(localStart, zone), TimeZoneInfo.ConvertTimeToUtc(localEnd, zone)));
                }
                cursor += TimeSpan.FromMinutes(slotIntervalMinutes);
            }
        }
        return result;
    }
}
