namespace Shine.Domain;

/// <summary>Stable identity and presentation metadata for a dashboard widget published by a module.</summary>
public sealed class DashboardWidgetDescriptor
{
    public DashboardWidgetDescriptor(string widgetKey, string moduleKey, string title, string description, string requiredPermission, int defaultWidth = 2, int defaultHeight = 1, int minWidth = 1, int minHeight = 1)
    {
        WidgetKey = NormalizeWidgetKey(widgetKey, nameof(widgetKey)); ModuleKey = Normalize(moduleKey, nameof(moduleKey)); Title = Require(title, nameof(title)); Description = Require(description, nameof(description)); RequiredPermission = Require(requiredPermission, nameof(requiredPermission)); DefaultWidth = defaultWidth; DefaultHeight = defaultHeight; MinWidth = minWidth; MinHeight = minHeight; Validate();
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

    private static string Normalize(string value, string parameterName) => new ModuleCode(value).Value;
    private static string NormalizeWidgetKey(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Value is required.", parameterName);
        var normalized = value.Trim().ToUpperInvariant();
        if (normalized.Any(c => !(char.IsLetterOrDigit(c) || c is '.' or '-' or '_')))
            throw new ArgumentException("Widget key may contain only letters, numbers, '.', '-' or '_'.", parameterName);
        return normalized;
    }
    private static string Require(string value, string parameterName) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", parameterName) : value.Trim();
}

public sealed record DashboardWidgetData
{
    public DashboardWidgetData(string widgetKey, IReadOnlyDictionary<string, object?> values)
    {
        if (string.IsNullOrWhiteSpace(widgetKey)) throw new ArgumentException("Widget key is required.", nameof(widgetKey));
        ArgumentNullException.ThrowIfNull(values);
        WidgetKey = widgetKey.Trim().ToUpperInvariant();
        Values = values;
    }

    public string WidgetKey { get; }
    public IReadOnlyDictionary<string, object?> Values { get; }
}

public sealed record DashboardWidgetContext
{
    public DashboardWidgetContext(Guid tenantId, Guid? userId, DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (fromUtc.Offset != TimeSpan.Zero || toUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Widget context must use UTC offsets.");
        if (fromUtc >= toUtc) throw new ArgumentException("Widget context period is invalid.");
        TenantId = tenantId; UserId = userId; FromUtc = fromUtc; ToUtc = toUtc;
    }

    public Guid TenantId { get; }
    public Guid? UserId { get; }
    public DateTimeOffset FromUtc { get; }
    public DateTimeOffset ToUtc { get; }
}

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
