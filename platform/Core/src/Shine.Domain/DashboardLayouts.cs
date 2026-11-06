namespace Shine.Domain;

public sealed class DashboardLayout : AuditableEntity, IMultiTenantEntity
{
    private DashboardLayout() { }
    public DashboardLayout(Guid tenantId, Guid? userId, string dashboardKey)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        TenantId = tenantId; UserId = userId; DashboardKey = Normalize(dashboardKey); Version = 1;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid TenantId { get; private set; }
    public Guid? UserId { get; private set; }
    public string DashboardKey { get; private set; } = null!;
    public int Version { get; private set; }
    public ICollection<DashboardWidgetPlacement> Placements { get; private set; } = new List<DashboardWidgetPlacement>();
    public void AdvanceVersion() => Version++;
    public void ReplacePlacements(IEnumerable<DashboardWidgetPlacement> placements)
    {
        var values = placements.ToArray();
        Validate(values);
        var replacements = values.ToDictionary(x => x.WidgetKey, StringComparer.OrdinalIgnoreCase);
        foreach (var removed in Placements.Where(x => !replacements.ContainsKey(x.WidgetKey)).ToArray())
            Placements.Remove(removed);
        foreach (var replacement in values)
        {
            var existing = Placements.SingleOrDefault(x => string.Equals(x.WidgetKey, replacement.WidgetKey, StringComparison.OrdinalIgnoreCase));
            if (existing is null) Placements.Add(replacement);
            else existing.UpdateFrom(replacement);
        }
        Version++;
    }
    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Dashboard key is required.", nameof(value)) : value.Trim().ToUpperInvariant();
    private static void Validate(IReadOnlyCollection<DashboardWidgetPlacement> placements)
    {
        if (placements.Count > 100) throw new ArgumentException("A layout cannot contain more than 100 widgets.", nameof(placements));
        if (placements.GroupBy(x => x.WidgetKey, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) throw new ArgumentException("A layout cannot contain duplicate widgets.", nameof(placements));
        var visible = placements.Where(x => x.IsVisible).ToArray();
        for (var i = 0; i < visible.Length; i++) for (var j = i + 1; j < visible.Length; j++)
            if (visible[i].Intersects(visible[j])) throw new ArgumentException("Visible widgets cannot overlap.", nameof(placements));
    }
}

public sealed class DashboardWidgetPlacement
{
    private DashboardWidgetPlacement() { }
    public DashboardWidgetPlacement(string widgetKey, string moduleKey, int positionX, int positionY, int width, int height, bool isVisible = true, string settingsJson = "{}")
    {
        WidgetKey = Normalize(widgetKey); ModuleKey = Normalize(moduleKey);
        if (positionX < 0 || positionY < 0 || width < 1 || height < 1) throw new ArgumentException("Widget position and dimensions are invalid.");
        if (settingsJson is null || settingsJson.Length > 16000) throw new ArgumentException("Widget settings are invalid.", nameof(settingsJson));
        PositionX = positionX; PositionY = positionY; Width = width; Height = height; IsVisible = isVisible; SettingsJson = settingsJson;
    }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid LayoutId { get; private set; }
    public string WidgetKey { get; private set; } = null!;
    public string ModuleKey { get; private set; } = null!;
    public int PositionX { get; private set; }
    public int PositionY { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public bool IsVisible { get; private set; }
    public string SettingsJson { get; private set; } = "{}";
    internal void UpdateFrom(DashboardWidgetPlacement replacement)
    {
        ModuleKey = replacement.ModuleKey;
        PositionX = replacement.PositionX;
        PositionY = replacement.PositionY;
        Width = replacement.Width;
        Height = replacement.Height;
        IsVisible = replacement.IsVisible;
        SettingsJson = replacement.SettingsJson;
    }
    public bool Intersects(DashboardWidgetPlacement other) => PositionX < other.PositionX + other.Width && PositionX + Width > other.PositionX && PositionY < other.PositionY + other.Height && PositionY + Height > other.PositionY;
    private static string Normalize(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Widget key is required.") : value.Trim().ToUpperInvariant();
}
