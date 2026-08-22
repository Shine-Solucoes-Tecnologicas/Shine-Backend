using Shine.Domain;

namespace Shine.UnitTests;

public sealed class TimeWindowTests
{
    [Fact]
    public void Detects_overlapping_windows()
    {
        var first = new TimeWindow(DateTime.SpecifyKind(new DateTime(2026, 8, 10, 10, 0, 0), DateTimeKind.Utc), DateTime.SpecifyKind(new DateTime(2026, 8, 10, 11, 0, 0), DateTimeKind.Utc));
        var second = new TimeWindow(DateTime.SpecifyKind(new DateTime(2026, 8, 10, 10, 30, 0), DateTimeKind.Utc), DateTime.SpecifyKind(new DateTime(2026, 8, 10, 11, 30, 0), DateTimeKind.Utc));

        Assert.True(first.Overlaps(second));
        Assert.Equal(TimeSpan.FromHours(1), first.Duration);
    }

    [Fact]
    public void Adjacent_windows_do_not_overlap()
    {
        var first = new TimeWindow(DateTime.SpecifyKind(new DateTime(2026, 8, 10, 10, 0, 0), DateTimeKind.Utc), DateTime.SpecifyKind(new DateTime(2026, 8, 10, 11, 0, 0), DateTimeKind.Utc));
        var second = new TimeWindow(DateTime.SpecifyKind(new DateTime(2026, 8, 10, 11, 0, 0), DateTimeKind.Utc), DateTime.SpecifyKind(new DateTime(2026, 8, 10, 12, 0, 0), DateTimeKind.Utc));

        Assert.False(first.Overlaps(second));
    }
}
