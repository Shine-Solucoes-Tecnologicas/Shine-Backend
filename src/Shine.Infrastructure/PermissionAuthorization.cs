using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IPermissionAuthorization
{
    Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default);
    Task<bool> HasGlobalPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(false);
    Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

public sealed class PermissionAuthorization(ShineDbContext db) : IPermissionAuthorization
{
    public async Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default)
    {
        if (await IsPlatformOperatorAsync(userId, cancellationToken)) return false;
        return await db.UserTenantRoles.AnyAsync(link => link.UserId == userId && link.TenantId == tenantId && link.Role.TenantId == tenantId && link.Role.Permissions.Any(permission => permission.Permission.Code == permissionCode), cancellationToken);
    }

    public Task<bool> HasGlobalPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) =>
        db.UserGlobalRoles.AnyAsync(link => link.UserId == userId && link.Role.Permissions.Any(permission => permission.Permission.Code == permissionCode), cancellationToken);

    public Task<bool> IsPlatformOperatorAsync(Guid userId, CancellationToken cancellationToken = default) =>
        db.UserGlobalRoles.AnyAsync(link => link.UserId == userId, cancellationToken);
}
