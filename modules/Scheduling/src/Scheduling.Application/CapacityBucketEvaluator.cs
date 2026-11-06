namespace Scheduling.Application;

public sealed record OccupiedPeriod(DateTime StartUtc, DateTime EndUtc);
public sealed record CapacityBucketEvaluation(int ResultingPeakOccupancy, IReadOnlyCollection<TimeBucket> SaturatedBuckets)
{
    public bool Exceeded => SaturatedBuckets.Count > 0;
}

public static class CapacityBucketEvaluator
{
    private const int MaximumReportedBuckets = 50;

    public static CapacityBucketEvaluation Evaluate(
        DateTime candidateStartUtc,
        DateTime candidateEndUtc,
        string timeZoneId,
        int gridIntervalMinutes,
        int bufferBeforeMinutes,
        int bufferAfterMinutes,
        int capacity,
        IReadOnlyCollection<OccupiedPeriod> appointments)
    {
        if (candidateStartUtc.Kind != DateTimeKind.Utc || candidateEndUtc.Kind != DateTimeKind.Utc || candidateStartUtc >= candidateEndUtc)
            throw new ArgumentException("Candidate period must be a valid UTC interval.");
        if (capacity is <= 0 or > 100) throw new ArgumentOutOfRangeException(nameof(capacity));

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var candidateOccupied = new OccupiedPeriod(
            candidateStartUtc.AddMinutes(-bufferBeforeMinutes), candidateEndUtc.AddMinutes(bufferAfterMinutes));
        var firstDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(candidateOccupied.StartUtc, zone));
        var lastInstant = candidateOccupied.EndUtc.AddTicks(-1);
        var lastDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(lastInstant, zone));
        var buckets = new List<TimeBucket>();
        for (var date = firstDate; date <= lastDate; date = date.AddDays(1))
            buckets.AddRange(LocalTimeGrid.Buckets(date, timeZoneId, gridIntervalMinutes)
                .Where(bucket => Intersects(candidateOccupied.StartUtc, candidateOccupied.EndUtc, bucket.StartUtc, bucket.EndUtc)));

        var occupied = appointments
            .Select(x => new OccupiedPeriod(x.StartUtc.AddMinutes(-bufferBeforeMinutes), x.EndUtc.AddMinutes(bufferAfterMinutes)))
            .ToArray();
        var peak = 1;
        var saturated = new List<TimeBucket>();
        foreach (var bucket in buckets.DistinctBy(x => x.StartUtc).OrderBy(x => x.StartUtc))
        {
            var resulting = 1 + occupied.Count(x => Intersects(x.StartUtc, x.EndUtc, bucket.StartUtc, bucket.EndUtc));
            peak = Math.Max(peak, resulting);
            if (resulting > capacity && saturated.Count < MaximumReportedBuckets) saturated.Add(bucket);
        }
        return new(peak, saturated);
    }

    private static bool Intersects(DateTime leftStart, DateTime leftEnd, DateTime rightStart, DateTime rightEnd) =>
        leftStart < rightEnd && leftEnd > rightStart;
}
