namespace Shine.Domain;

/// <summary>Stable identity and presentation metadata for a dashboard widget published by a module.</summary>
public sealed class DashboardWidgetDescriptor
{
    public DashboardWidgetDescriptor(string widgetKey, string moduleKey, string title, string description, string requiredPermission, int defaultWidth = 2, int defaultHeight = 1, int minWidth = 1, int minHeight = 1)
    {
        WidgetKey = Normalize(widgetKey, nameof(widgetKey)); ModuleKey = Normalize(moduleKey, nameof(moduleKey)); Title = Require(title, nameof(title)); Description = Require(description, nameof(description)); RequiredPermission = Require(requiredPermission, nameof(requiredPermission)); DefaultWidth = defaultWidth; DefaultHeight = defaultHeight; MinWidth = minWidth; MinHeight = minHeight; Validate();
    }
    public string WidgetKey { get; }
    public string ModuleKey { get; }
    public string Title { get; }
    public string Description { get; }
    public string RequiredPermission { get; }
    public int DefaultWidth { get; }
    public int DefaultHeight { get; }
    public int MinWidth { get; }
    public int MinHeight { get; }

    public DashboardWidgetDescriptor Validate()
    {
        if (DefaultWidth < MinWidth || DefaultHeight < MinHeight)
            throw new ArgumentException("Default widget size cannot be smaller than its minimum size.");
        if (MinWidth < 1 || MinHeight < 1)
            throw new ArgumentException("Widget minimum size must be positive.");
        return this;
    }

    private static string Normalize(string value, string parameterName) => new ModuleCode(value).Value.Length == 0 ? throw new ArgumentException("Value is required.", parameterName) : new ModuleCode(value).Value;
    private static string Require(string value, string parameterName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", parameterName) : value.Trim();
}

public sealed record DashboardWidgetData(string WidgetKey, IReadOnlyDictionary<string, object?> Values);

public sealed record DashboardWidgetContext(Guid TenantId, Guid? UserId, DateTimeOffset FromUtc, DateTimeOffset ToUtc);

public interface IDashboardWidgetProvider
{
    DashboardWidgetDescriptor Descriptor { get; }
    Task<DashboardWidgetData> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken = default);
}

public interface IDashboardWidgetCatalog
{
    IReadOnlyCollection<DashboardWidgetDescriptor> Descriptors { get; }
    void Register(IDashboardWidgetProvider provider);
    IDashboardWidgetProvider Get(string widgetKey);
}
