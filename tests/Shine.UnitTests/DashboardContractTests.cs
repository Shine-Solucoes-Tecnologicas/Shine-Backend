using Shine.Domain;

namespace Shine.UnitTests;

public sealed class DashboardContractTests
{
    [Fact]
    public void Descriptor_normalizes_widget_and_module_keys()
    {
        var descriptor = new DashboardWidgetDescriptor(" agenda.next ", " scheduling ", "Next", "Next appointments", "scheduling.read");

        Assert.Equal("AGENDA.NEXT", descriptor.WidgetKey);
        Assert.Equal("SCHEDULING", descriptor.ModuleKey);
    }

    [Fact]
    public void Descriptor_rejects_default_size_below_minimum()
    {
        Assert.Throws<ArgumentException>(() => new DashboardWidgetDescriptor("agenda.next", "scheduling", "Next", "Next appointments", "scheduling.read", 1, 1, 2, 1));
    }

    [Fact]
    public void Context_requires_tenant_and_valid_utc_period()
    {
        Assert.Throws<ArgumentException>(() => new DashboardWidgetContext(Guid.Empty, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Throws<ArgumentException>(() => new DashboardWidgetContext(Guid.NewGuid(), null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-1)));
    }

    [Fact]
    public void Data_normalizes_widget_key_and_preserves_values()
    {
        var data = new DashboardWidgetData(" agenda.next ", new Dictionary<string, object?> { ["count"] = 2 });

        Assert.Equal("AGENDA.NEXT", data.WidgetKey);
        Assert.Equal(2, data.Values["count"]);
    }
}
