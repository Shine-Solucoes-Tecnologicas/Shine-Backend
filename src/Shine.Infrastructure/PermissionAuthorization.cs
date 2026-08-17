using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IPermissionAuthorization
{
    Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default);
    Task<bool> HasGlobalPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

public sealed class PermissionAuthorization(ShineDbContext db, ICustomerAccountAuthorization customerAccounts) : IPermissionAuthorization
{
    public async Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default)
    {
        if (await IsPlatformOperatorAsync(userId, cancellationToken)) return false;
        var accountId = await db.Tenants.AsNoTracking()
            .Where(tenant => tenant.Id == tenantId)
            .Select(tenant => tenant.CustomerAccountId)
            .SingleOrDefaultAsync(cancellationToken);

        if (accountId is Guid customerAccountId)
        {
            var moduleCode = PermissionModule(permissionCode);
            return await customerAccounts.HasPermissionAsync(userId, customerAccountId, permissionCode, tenantId, moduleCode, cancellationToken);
        }

        // Compatibility path for databases that have not yet associated a legacy tenant
        // with a customer account. The authorization migration backfills this association.
        return await db.UserTenantRoles.AnyAsync(link => link.UserId == userId && link.TenantId == tenantId &&
            link.Role.TenantId == tenantId && link.Role.Permissions.Any(permission => permission.Permission.Code == permissionCode), cancellationToken);
    }

    public Task<bool> HasGlobalPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) =>
        db.UserGlobalRoles.AnyAsync(link => link.UserId == userId && link.Role.Permissions.Any(permission => permission.Permission.Code == permissionCode), cancellationToken);

    public Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.UserGlobalRoles.AnyAsync(link => link.UserId == userId, cancellationToken);

    private static string? PermissionModule(string permissionCode) =>
        permissionCode.StartsWith("scheduling.", StringComparison.Ordinal) ? "SCHEDULING" : null;
}
