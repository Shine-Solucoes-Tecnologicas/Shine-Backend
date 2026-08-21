using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;
using System.Text.Json;

namespace Shine.Infrastructure;

public sealed record DashboardLayoutResult(Guid Id, Guid TenantId, Guid? UserId, string DashboardKey, int Version, IReadOnlyCollection<DashboardWidgetPlacement> Placements);

public sealed class DashboardLayoutService(ShineDbContext db, IDashboardWidgetCatalog catalog, IDashboardWidgetResolver resolver)
{
    public async Task<DashboardLayoutResult?> GetResolvedAsync(Guid tenantId, Guid userId, string dashboardKey, CancellationToken cancellationToken = default)
    {
        var key = dashboardKey.Trim().ToUpperInvariant();
        var layout = await db.DashboardLayouts.Include(x => x.Placements)
            .Where(x => x.TenantId == tenantId && x.DashboardKey == key && (x.UserId == null || x.UserId == userId))
            .OrderByDescending(x => x.UserId == userId)
            .FirstOrDefaultAsync(cancellationToken);
        if (layout is null) return null;
        var available = (await resolver.ResolveAsync(tenantId, userId, cancellationToken))
            .Select(x => x.Descriptor.WidgetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return ToResult(layout, layout.Placements.Where(x => available.Contains(x.WidgetKey)).ToArray());
    }

    public async Task<DashboardLayoutResult> SaveAsync(Guid tenantId, Guid userId, DashboardLayoutRequest request, CancellationToken cancellationToken = default)
    {
        var key = request.DashboardKey.Trim().ToUpperInvariant();
        var available = (await resolver.ResolveAsync(tenantId, userId, cancellationToken))
            .Select(x => x.Descriptor.WidgetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in request.Placements)
        {
            var descriptor = catalog.Descriptors.SingleOrDefault(x => string.Equals(x.WidgetKey, placement.WidgetKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Widget '{placement.WidgetKey}' is not registered.");
            if (!available.Contains(descriptor.WidgetKey)) throw new ArgumentException($"Widget '{descriptor.WidgetKey}' is not available to this user.");
            if (!string.Equals(placement.ModuleKey, descriptor.ModuleKey, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Widget '{descriptor.WidgetKey}' has an invalid module.");
            if (placement.Width < descriptor.MinWidth || placement.Height < descriptor.MinHeight) throw new ArgumentException($"Widget '{descriptor.WidgetKey}' is smaller than its minimum size.");
            try { using var settings = JsonDocument.Parse(placement.SettingsJson); if (settings.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
            catch (JsonException) { throw new ArgumentException($"Widget '{descriptor.WidgetKey}' settings must be a JSON object."); }
        }
        var layout = await db.DashboardLayouts.Include(x => x.Placements).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == key, cancellationToken);
        if (layout is null) { layout = new DashboardLayout(tenantId, userId, key); db.DashboardLayouts.Add(layout); }
        layout.ReplacePlacements(request.Placements);
        await db.SaveChangesAsync(cancellationToken);
        return ToResult(layout, layout.Placements.ToArray());
    }
    private static DashboardLayoutResult ToResult(DashboardLayout layout, IReadOnlyCollection<DashboardWidgetPlacement> placements) => new(layout.Id, layout.TenantId, layout.UserId, layout.DashboardKey, layout.Version, placements);
}

public sealed record DashboardLayoutRequest(string DashboardKey, IReadOnlyCollection<DashboardWidgetPlacement> Placements);
