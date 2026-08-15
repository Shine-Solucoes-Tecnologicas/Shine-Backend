using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public sealed record DashboardLayoutResult(Guid Id, Guid TenantId, Guid? UserId, string DashboardKey, int Version, IReadOnlyCollection<DashboardWidgetPlacement> Placements);

public sealed class DashboardLayoutService(ShineDbContext db)
{
    public async Task<DashboardLayoutResult?> GetResolvedAsync(Guid tenantId, Guid userId, string dashboardKey, CancellationToken cancellationToken = default)
    {
        var key = dashboardKey.Trim().ToUpperInvariant();
        var layouts = await db.DashboardLayouts.Include(x => x.Placements).Where(x => x.TenantId == tenantId && x.DashboardKey == key && (x.UserId == null || x.UserId == userId)).OrderBy(x => x.UserId == null).ToArrayAsync(cancellationToken);
        var layout = layouts.LastOrDefault();
        return layout is null ? null : ToResult(layout);
    }

    public async Task<DashboardLayoutResult> SaveAsync(Guid tenantId, Guid userId, DashboardLayoutRequest request, CancellationToken cancellationToken = default)
    {
        var key = request.DashboardKey.Trim().ToUpperInvariant();
        var layout = await db.DashboardLayouts.Include(x => x.Placements).SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.DashboardKey == key, cancellationToken);
        if (layout is null) { layout = new DashboardLayout(tenantId, userId, key); db.DashboardLayouts.Add(layout); }
        layout.ReplacePlacements(request.Placements);
        await db.SaveChangesAsync(cancellationToken);
        return ToResult(layout);
    }
    private static DashboardLayoutResult ToResult(DashboardLayout layout) => new(layout.Id, layout.TenantId, layout.UserId, layout.DashboardKey, layout.Version, layout.Placements.ToArray());
}

public sealed record DashboardLayoutRequest(string DashboardKey, IReadOnlyCollection<DashboardWidgetPlacement> Placements);
