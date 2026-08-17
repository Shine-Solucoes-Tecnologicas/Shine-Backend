using Shine.Domain;

namespace Shine.UnitTests;

public sealed class DashboardLayoutTests
{
    [Fact]
    public void Layout_rejects_duplicate_or_overlapping_visible_widgets()
    {
        var layout = new DashboardLayout(Guid.NewGuid(), Guid.NewGuid(), "main");
        var first = new DashboardWidgetPlacement("agenda.next", "scheduling", 0, 0, 2, 1);
        Assert.Throws<ArgumentException>(() => layout.ReplacePlacements([first, new DashboardWidgetPlacement("agenda.stats", "scheduling", 1, 0, 2, 1)]));
    }

    [Fact]
    public void Layout_allows_overlapping_hidden_widgets_and_increments_version()
    {
        var layout = new DashboardLayout(Guid.NewGuid(), null, "main");
        layout.ReplacePlacements([new DashboardWidgetPlacement("agenda.next", "scheduling", 0, 0, 2, 1), new DashboardWidgetPlacement("agenda.stats", "scheduling", 0, 0, 2, 1, false)]);

        Assert.Equal(2, layout.Version);
        Assert.Equal(2, layout.Placements.Count);
    }
}
