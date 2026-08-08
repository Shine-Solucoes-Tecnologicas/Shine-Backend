using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IPermissionAuthorization
{
    Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default);
}

public sealed class PermissionAuthorization(ShineDbContext db) : IPermissionAuthorization
{
    public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default) =>
        db.UserTenantRoles.AnyAsync(link => link.UserId == userId && link.TenantId == tenantId && link.Role.TenantId == tenantId && link.Role.Permissions.Any(permission => permission.Permission.Code == permissionCode), cancellationToken);
}
