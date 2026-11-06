namespace Scheduling.Application;

public sealed record LocalInstant(DateTime Utc, TimeSpan UtcOffset, bool IsAmbiguousLocalTime);
public sealed record UtcDayWindow(DateTime StartUtc, DateTime EndUtc);
public sealed record TimeBucket(DateTime StartUtc, DateTime EndUtc);

public static class LocalTimeGrid
{
    public static IReadOnlyCollection<LocalInstant> Resolve(DateOnly date, TimeSpan localTime, string timeZoneId) =>
        Resolve(date.ToDateTime(TimeOnly.FromTimeSpan(localTime), DateTimeKind.Unspecified),
            TimeZoneInfo.FindSystemTimeZoneById(timeZoneId));

    public static IReadOnlyCollection<LocalInstant> Resolve(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified)) return [];
        if (zone.IsAmbiguousTime(unspecified))
        {
            return zone.GetAmbiguousTimeOffsets(unspecified)
                .Select(offset => new LocalInstant(new DateTimeOffset(unspecified, offset).UtcDateTime, offset, true))
                .DistinctBy(x => x.Utc)
                .OrderBy(x => x.Utc)
                .ToArray();
        }

        var offset = zone.GetUtcOffset(unspecified);
        return [new(new DateTimeOffset(unspecified, offset).UtcDateTime, offset, false)];
    }

    public static UtcDayWindow? GetDayWindow(DateOnly date, string timeZoneId)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var start = FirstValidInstant(date, zone);
        if (start is null) return null;

        var nextDate = date.AddDays(1);
        LocalInstant? end = null;
        for (var offset = 0; offset <= 366 && end is null; offset++)
            end = FirstValidInstant(nextDate.AddDays(offset), zone);
        return end is null ? null : new(start.Utc, end.Utc);
    }

    public static IReadOnlyCollection<TimeBucket> Buckets(DateOnly date, string timeZoneId, int intervalMinutes)
    {
        if (intervalMinutes is <= 0 or > 120) throw new ArgumentOutOfRangeException(nameof(intervalMinutes));
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var interval = TimeSpan.FromMinutes(intervalMinutes);
        var buckets = new List<TimeBucket>();
        for (var localTime = TimeSpan.Zero; localTime < TimeSpan.FromDays(1); localTime += interval)
        {
            var local = date.ToDateTime(TimeOnly.FromTimeSpan(localTime), DateTimeKind.Unspecified);
            foreach (var instant in Resolve(local, zone))
                buckets.Add(new(instant.Utc, instant.Utc.Add(interval)));
        }
        return buckets.DistinctBy(x => x.StartUtc).OrderBy(x => x.StartUtc).ToArray();
    }

    private static LocalInstant? FirstValidInstant(DateOnly date, TimeZoneInfo zone)
    {
        for (var minute = 0; minute < 24 * 60; minute++)
        {
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified).AddMinutes(minute);
            var instants = Resolve(local, zone);
            if (instants.Count > 0) return instants.MinBy(x => x.Utc);
        }
        return null;
    }
}
