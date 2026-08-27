using Microsoft.EntityFrameworkCore;
using Shine.Domain.Authorization;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IPermissionAuthorization
{
    Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default);
    async Task<PermissionScope?> GetPermissionScopeAsync(Guid userId, Guid tenantId, string permissionCode,
        CancellationToken cancellationToken = default) =>
        await HasPermissionAsync(userId, tenantId, permissionCode, cancellationToken) ? PermissionScope.All : null;
    Task<bool> HasGlobalPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

public sealed class PermissionAuthorization(ShineDbContext db, ICustomerAccountAuthorization customerAccounts) : IPermissionAuthorization
{
    public async Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode,
        CancellationToken cancellationToken = default) =>
        await GetPermissionScopeAsync(userId, tenantId, permissionCode, cancellationToken) is not null;

    public async Task<PermissionScope?> GetPermissionScopeAsync(Guid userId, Guid tenantId, string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (await IsPlatformOperatorAsync(userId, cancellationToken)) return null;
        var hasActiveTenantMembership = await db.UserTenants.AsNoTracking().AnyAsync(link =>
            link.UserId == userId && link.TenantId == tenantId && link.IsActive && link.Tenant.IsActive,
            cancellationToken);
        if (!hasActiveTenantMembership) return null;

        var accountId = await db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.CustomerAccountId)
            .SingleOrDefaultAsync(cancellationToken);

        if (accountId is Guid customerAccountId)
        {
            var moduleCode = PermissionModule(permissionCode);
            return await customerAccounts.GetPermissionScopeAsync(userId, customerAccountId, permissionCode, tenantId, moduleCode, cancellationToken);
        }

        // Compatibility path for databases that have not yet associated a legacy tenant
        // with a customer account. The authorization migration backfills this association.
        var scopes = await db.UserTenantRoles.AsNoTracking()
            .Where(link => link.UserId == userId && link.TenantId == tenantId && link.Role.TenantId == tenantId)
            .SelectMany(link => link.Role.Permissions
                .Where(permission => permission.Permission.Code == permissionCode)
                .Select(permission => permission.Scope))
            .Distinct()
            .ToArrayAsync(cancellationToken);
        if (scopes.Contains(PermissionScope.All)) return PermissionScope.All;
        return scopes.Contains(PermissionScope.Own) ? PermissionScope.Own : null;
    }

    public Task<bool> HasGlobalPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) =>
        db.UserGlobalRoles.AnyAsync(link => link.UserId == userId && link.Role.Permissions.Any(permission => permission.Permission.Code == permissionCode), cancellationToken);

    public Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.UserGlobalRoles.AnyAsync(link => link.UserId == userId, cancellationToken);

    private static string? PermissionModule(string permissionCode) =>
        permissionCode.StartsWith("scheduling.", StringComparison.Ordinal) ? "SCHEDULING" : null;
}
