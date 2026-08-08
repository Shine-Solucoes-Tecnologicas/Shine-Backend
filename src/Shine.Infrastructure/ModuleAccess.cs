using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IModuleAccess
{
    Task<IReadOnlyCollection<ModuleDescriptor>> GetAccessibleAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default);
    Task SetAsync(Guid tenantId, string moduleCode, bool enabled, CancellationToken cancellationToken = default);
}

public sealed class ModuleAccessService(ShineDbContext db, IModuleCatalog catalog) : IModuleAccess
{
    public async Task<IReadOnlyCollection<ModuleDescriptor>> GetAccessibleAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        var enabled = await db.ModuleAccesses.AsNoTracking()
            .Where(access => access.TenantId == tenantId && access.Enabled)
            .Select(access => access.ModuleCode)
            .ToHashSetAsync(cancellationToken);
        return catalog.Modules.Where(module => enabled.Contains(module.Code.Value)).ToArray();
    }

    public Task<bool> HasAccessAsync(Guid tenantId, string moduleCode, CancellationToken cancellationToken = default)
    {
        var normalized = new ModuleCode(moduleCode).Value;
        return db.ModuleAccesses.AnyAsync(access => access.TenantId == tenantId && access.ModuleCode == normalized && access.Enabled, cancellationToken);
    }

    public async Task SetAsync(Guid tenantId, string moduleCode, bool enabled, CancellationToken cancellationToken = default)
    {
        var normalized = new ModuleCode(moduleCode).Value;
        if (!catalog.Modules.Any(module => module.Code.Value == normalized))
            throw new KeyNotFoundException($"Module '{normalized}' is not registered.");

        var access = await db.ModuleAccesses.SingleOrDefaultAsync(item => item.TenantId == tenantId && item.ModuleCode == normalized, cancellationToken);
        if (access is null) db.ModuleAccesses.Add(new Domain.ModuleAccess(tenantId, normalized, enabled));
        else access.SetEnabled(enabled);
        await db.SaveChangesAsync(cancellationToken);
    }
}
