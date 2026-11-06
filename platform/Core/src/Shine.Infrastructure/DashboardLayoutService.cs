using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Shine.Infrastructure;

public sealed record DashboardWidgetPlacementResult(string WidgetKey, string ModuleKey, int PositionX, int PositionY,
    int Width, int Height, bool IsVisible, string SettingsJson);
public sealed record DashboardLayoutResult(Guid? Id, Guid TenantId, Guid? UserId, string DashboardKey, string Source,
    int Version, int? SourceVersion, string ManifestVersion, IReadOnlyCollection<DashboardWidgetPlacementResult> Placements);

public sealed class StaleDashboardLayoutException(int? currentVersion) : Exception("The dashboard layout has changed since it was loaded.")
{
    public int? CurrentVersion { get; } = currentVersion;
}

public sealed class DashboardLayoutService(ShineDbContext db, IDashboardWidgetCatalog catalog, IDashboardWidgetResolver resolver)
{
    public async Task<DashboardLayoutResult> GetResolvedAsync(Guid tenantId, Guid userId, string dashboardKey, CancellationToken cancellationToken = default)
    {
        var key = dashboardKey.Trim().ToUpperInvariant();
        var availableWidgets = (await resolver.ResolveAsync(tenantId, userId, cancellationToken))
            .Select(x => x.Descriptor)
            .OrderBy(x => x.DefaultOrder).ThenBy(x => x.WidgetKey, StringComparer.Ordinal)
            .ToArray();
        var available = availableWidgets.Select(x => x.WidgetKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var manifestVersion = ManifestVersion(availableWidgets);
        var layouts = await db.DashboardLayouts.Include(x => x.Placements)
            .Where(x => x.TenantId == tenantId && x.DashboardKey == key && (x.UserId == null || x.UserId == userId))
            .OrderByDescending(x => x.UserId == userId)
            .ToArrayAsync(cancellationToken);
        var userLayout = layouts.FirstOrDefault(x => x.UserId == userId);
        if (userLayout is not null)
            return ToResult(userLayout, "user", userLayout.Version, manifestVersion,
                userLayout.Placements.Where(x => available.Contains(x.WidgetKey)).ToArray());
        var tenantLayout = layouts.FirstOrDefault(x => x.UserId is null);
        var tenantPlacements = tenantLayout?.Placements.Where(x => available.Contains(x.WidgetKey)).ToArray();
        if (tenantLayout is not null && tenantPlacements is { Length: > 0 })
            return ToResult(tenantLayout, "tenant", 0, manifestVersion, tenantPlacements);
        return SystemDefault(tenantId, key, manifestVersion, availableWidgets);
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
        var layout = await db.DashboardLayouts.IgnoreQueryFilters().Include(x => x.Placements)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == key, cancellationToken);
        if (layout is null)
        {
            if (request.ExpectedVersion != 0) throw new StaleDashboardLayoutException(null);
            layout = new DashboardLayout(tenantId, userId, key);
            db.DashboardLayouts.Add(layout);
        }
        else if (layout.IsDeleted && request.ExpectedVersion == 0)
        {
            layout.Restore();
        }
        else if (layout.Version != request.ExpectedVersion)
        {
            throw new StaleDashboardLayoutException(layout.Version);
        }
        layout.ReplacePlacements(request.Placements);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var currentVersion = await db.DashboardLayouts.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == key)
                .Select(x => (int?)x.Version)
                .SingleOrDefaultAsync(cancellationToken);
            throw new StaleDashboardLayoutException(currentVersion);
        }
        var descriptors = (await resolver.ResolveAsync(tenantId, userId, cancellationToken)).Select(x => x.Descriptor).ToArray();
        return ToResult(layout, "user", layout.Version, ManifestVersion(descriptors), layout.Placements.ToArray());
    }

    public async Task<DashboardLayoutResult> ResetAsync(Guid tenantId, Guid userId, DashboardLayoutResetRequest request,
        CancellationToken cancellationToken = default)
    {
        var key = request.DashboardKey.Trim().ToUpperInvariant();
        var layout = await db.DashboardLayouts.Include(x => x.Placements)
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == key, cancellationToken);
        if (layout is null) return await GetResolvedAsync(tenantId, userId, key, cancellationToken);
        if (layout.Version != request.ExpectedVersion) throw new StaleDashboardLayoutException(layout.Version);
        layout.AdvanceVersion();
        layout.Delete(DateTime.UtcNow);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            db.ChangeTracker.Clear();
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            var currentVersion = await db.DashboardLayouts.AsNoTracking()
                .Where(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == key)
                .Select(x => (int?)x.Version).SingleOrDefaultAsync(cancellationToken);
            if (currentVersion is not null) throw new StaleDashboardLayoutException(currentVersion);
        }
        return await GetResolvedAsync(tenantId, userId, key, cancellationToken);
    }

    private static DashboardLayoutResult ToResult(DashboardLayout layout, string source, int expectedVersion,
        string manifestVersion, IReadOnlyCollection<DashboardWidgetPlacement> placements) =>
        new(layout.Id, layout.TenantId, layout.UserId, layout.DashboardKey, source, expectedVersion, layout.Version,
            manifestVersion, placements.Select(ToResult).ToArray());

    private static DashboardWidgetPlacementResult ToResult(DashboardWidgetPlacement placement) =>
        new(placement.WidgetKey, placement.ModuleKey, placement.PositionX, placement.PositionY, placement.Width,
            placement.Height, placement.IsVisible, placement.SettingsJson);

    private static DashboardLayoutResult SystemDefault(Guid tenantId, string key, string manifestVersion,
        IReadOnlyCollection<DashboardWidgetDescriptor> descriptors)
    {
        const int columns = 12;
        var x = 0; var y = 0; var rowHeight = 0;
        var placements = new List<DashboardWidgetPlacementResult>();
        foreach (var descriptor in descriptors.OrderBy(item => item.DefaultOrder).ThenBy(item => item.WidgetKey, StringComparer.Ordinal))
        {
            var width = Math.Min(columns, descriptor.DefaultWidth);
            if (x + width > columns) { x = 0; y += rowHeight; rowHeight = 0; }
            placements.Add(new(descriptor.WidgetKey, descriptor.ModuleKey, x, y, width, descriptor.DefaultHeight, true, "{}"));
            x += width;
            rowHeight = Math.Max(rowHeight, descriptor.DefaultHeight);
        }
        return new(null, tenantId, null, key, "system", 0, null, manifestVersion, placements);
    }

    private static string ManifestVersion(IEnumerable<DashboardWidgetDescriptor> descriptors)
    {
        var manifest = string.Join('|', descriptors.OrderBy(x => x.DefaultOrder).ThenBy(x => x.WidgetKey, StringComparer.Ordinal)
            .Select(x => $"{x.WidgetKey}:{x.ModuleKey}:{x.DefaultOrder}:{x.DefaultWidth}:{x.DefaultHeight}:{x.SettingsSchemaVersion}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest)))[..16];
    }
}

public sealed record DashboardLayoutRequest(string DashboardKey, int ExpectedVersion, IReadOnlyCollection<DashboardWidgetPlacement> Placements);
public sealed record DashboardLayoutResetRequest(string DashboardKey, int ExpectedVersion);
