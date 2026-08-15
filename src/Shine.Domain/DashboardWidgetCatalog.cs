namespace Shine.Domain;

/// <summary>In-memory registry for widget providers published by modules.</summary>
public sealed class DashboardWidgetCatalog : IDashboardWidgetCatalog
{
    private readonly Dictionary<string, IDashboardWidgetProvider> providers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public IReadOnlyCollection<DashboardWidgetDescriptor> Descriptors
    {
        get { lock (sync) return providers.Values.Select(x => x.Descriptor).OrderBy(x => x.WidgetKey).ToArray(); }
    }

    public void Register(IDashboardWidgetProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var descriptor = provider.Descriptor ?? throw new InvalidOperationException("Widget provider must expose a descriptor.");
        lock (sync)
        {
            if (providers.ContainsKey(descriptor.WidgetKey)) throw new InvalidOperationException($"Dashboard widget '{descriptor.WidgetKey}' is already registered.");
            providers.Add(descriptor.WidgetKey, provider);
        }
    }

    public IDashboardWidgetProvider Get(string widgetKey)
    {
        if (string.IsNullOrWhiteSpace(widgetKey)) throw new ArgumentException("Widget key is required.", nameof(widgetKey));
        lock (sync) return providers.TryGetValue(widgetKey.Trim(), out var provider) ? provider : throw new KeyNotFoundException($"Dashboard widget '{widgetKey}' is not registered.");
    }
}
