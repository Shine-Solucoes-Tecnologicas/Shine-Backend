using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IFunctionalSettings
{
    Task<string?> GetAsync(string key, Guid? tenantId, CancellationToken cancellationToken = default);
    Task SetGlobalAsync(string key, string value, CancellationToken cancellationToken = default);
    Task SetForTenantAsync(Guid tenantId, string key, string value, CancellationToken cancellationToken = default);
}

public sealed class FunctionalSettings(ShineDbContext db) : IFunctionalSettings
{
    public async Task<string?> GetAsync(string key, Guid? tenantId, CancellationToken cancellationToken = default)
    {
        var normalized = new FunctionalSetting(key, string.Empty).Key;
        var values = await db.FunctionalSettings.AsNoTracking()
            .Where(item => item.Key == normalized && (item.TenantId == null || item.TenantId == tenantId))
            .OrderByDescending(item => item.TenantId != null)
            .Select(item => item.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return values;
    }

    public Task SetGlobalAsync(string key, string value, CancellationToken cancellationToken = default) => SetAsync(null, key, value, cancellationToken);
    public Task SetForTenantAsync(Guid tenantId, string key, string value, CancellationToken cancellationToken = default) => SetAsync(tenantId, key, value, cancellationToken);

    private async Task SetAsync(Guid? tenantId, string key, string value, CancellationToken cancellationToken)
    {
        var candidate = new FunctionalSetting(key, value, tenantId);
        var existing = await db.FunctionalSettings.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.Key == candidate.Key, cancellationToken);
        if (existing is null) db.FunctionalSettings.Add(candidate);
        else existing.UpdateValue(candidate.Value);
        await db.SaveChangesAsync(cancellationToken);
    }
}
