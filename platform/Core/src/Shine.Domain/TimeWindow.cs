namespace Shine.Domain;

public readonly record struct TimeWindow
{
    public TimeWindow(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (startsAtUtc.Kind != DateTimeKind.Utc || endsAtUtc.Kind != DateTimeKind.Utc || startsAtUtc >= endsAtUtc)
            throw new ArgumentException("Time window must be a valid UTC period.");
        StartsAtUtc = startsAtUtc;
        EndsAtUtc = endsAtUtc;
    }

    public DateTime StartsAtUtc { get; }
    public DateTime EndsAtUtc { get; }

    public TimeSpan Duration => EndsAtUtc - StartsAtUtc;
    public bool Overlaps(TimeWindow other) => StartsAtUtc < other.EndsAtUtc && EndsAtUtc > other.StartsAtUtc;
    public bool Contains(DateTime instantUtc) => instantUtc >= StartsAtUtc && instantUtc < EndsAtUtc;
}
