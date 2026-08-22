using Shine.Domain;

namespace Shine.UnitTests;

public sealed class DashboardWidgetCatalogTests
{
    [Fact]
    public void Catalog_normalizes_keys_and_rejects_duplicates()
    {
        var catalog = new DashboardWidgetCatalog();
        catalog.Register(new TestProvider(new DashboardWidgetDescriptor("agenda.next", "scheduling", "Next", "Next appointments", "scheduling.read")));

        Assert.Equal("AGENDA.NEXT", catalog.Descriptors.Single().WidgetKey);
        Assert.Same(catalog.Get("agenda.next"), catalog.Get("AGENDA.NEXT"));
        Assert.Throws<InvalidOperationException>(() => catalog.Register(new TestProvider(new DashboardWidgetDescriptor("AGENDA.NEXT", "scheduling", "Duplicate", "Duplicate", "scheduling.read"))));
    }

    [Fact]
    public void Catalog_rejects_unknown_widget()
    {
        var catalog = new DashboardWidgetCatalog();
        Assert.Throws<KeyNotFoundException>(() => catalog.Get("missing.widget"));
    }

    private sealed class TestProvider(DashboardWidgetDescriptor descriptor) : IDashboardWidgetProvider
    {
        public DashboardWidgetDescriptor Descriptor { get; } = descriptor;
        public Task<DashboardWidgetData> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken = default) => Task.FromResult(new DashboardWidgetData(Descriptor.WidgetKey, new Dictionary<string, object?>()));
    }
}
