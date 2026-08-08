using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IFeatureFlags
{
    Task<bool> IsEnabledAsync(string key, Guid? tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, bool>> GetAllAsync(Guid? tenantId, CancellationToken cancellationToken = default);
    Task SetGlobalAsync(string key, bool enabled, CancellationToken cancellationToken = default);
    Task SetForTenantAsync(Guid tenantId, string key, bool enabled, CancellationToken cancellationToken = default);
}

public sealed class FeatureFlags(ShineDbContext db) : IFeatureFlags
{
    public async Task<bool> IsEnabledAsync(string key, Guid? tenantId, CancellationToken cancellationToken = default)
        => (await GetAllAsync(tenantId, cancellationToken)).TryGetValue(new FeatureFlag(key, false).Key, out var enabled) && enabled;

    public async Task<IReadOnlyDictionary<string, bool>> GetAllAsync(Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var flags = await db.FeatureFlags.AsNoTracking()
            .Where(item => item.TenantId == null || item.TenantId == tenantId)
            .OrderBy(item => item.TenantId != null)
            .ToListAsync(cancellationToken);
        return flags.GroupBy(item => item.Key).ToDictionary(group => group.Key, group => group.Last().Enabled);
    }

    public Task SetGlobalAsync(string key, bool enabled, CancellationToken cancellationToken = default) => SetAsync(null, key, enabled, cancellationToken);
    public Task SetForTenantAsync(Guid tenantId, string key, bool enabled, CancellationToken cancellationToken = default) => SetAsync(tenantId, key, enabled, cancellationToken);

    private async Task SetAsync(Guid? tenantId, string key, bool enabled, CancellationToken cancellationToken)
    {
        var candidate = new FeatureFlag(key, enabled, tenantId);
        var existing = await db.FeatureFlags.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Key == candidate.Key, cancellationToken);
        if (existing is null) db.FeatureFlags.Add(candidate);
        else existing.SetEnabled(enabled);
        await db.SaveChangesAsync(cancellationToken);
    }
}
