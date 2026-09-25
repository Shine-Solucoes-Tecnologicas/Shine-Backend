using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Domain.Authorization;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface ICustomerAccountAuthorization
{
    Task<bool> HasPermissionAsync(Guid userId, Guid accountId, string permissionCode, Guid? unitId = null, string? moduleCode = null, CancellationToken cancellationToken = default);
    async Task<PermissionScope?> GetPermissionScopeAsync(Guid userId, Guid accountId, string permissionCode,
        Guid? unitId = null, string? moduleCode = null, CancellationToken cancellationToken = default) =>
        await HasPermissionAsync(userId, accountId, permissionCode, unitId, moduleCode, cancellationToken)
            ? PermissionScope.All : null;
}

public sealed class CustomerAccountAuthorization(ShineDbContext db) : ICustomerAccountAuthorization
{
    public async Task<bool> HasPermissionAsync(Guid userId, Guid accountId, string permissionCode, Guid? unitId = null,
        string? moduleCode = null, CancellationToken cancellationToken = default) =>
        await GetPermissionScopeAsync(userId, accountId, permissionCode, unitId, moduleCode, cancellationToken) is not null;

    public async Task<PermissionScope?> GetPermissionScopeAsync(Guid userId, Guid accountId, string permissionCode,
        Guid? unitId = null, string? moduleCode = null, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || accountId == Guid.Empty || string.IsNullOrWhiteSpace(permissionCode)) return null;
        if (await db.UserGlobalRoles.AnyAsync(x => x.UserId == userId, cancellationToken)) return null;

        var normalizedModule = moduleCode is null ? null : new ModuleCode(moduleCode).Value;
        var scopes = await db.CustomerAccountUserRoles.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.UserId == userId && x.Membership.IsActive && x.Role.AccountId == accountId &&
                (unitId == null || x.AllUnits || x.Units.Any(unit => unit.UnitId == unitId.Value)) &&
                (normalizedModule == null || x.AllModules || x.Modules.Any(module => module.ModuleCode == normalizedModule)))
            .SelectMany(x => x.Role.Permissions
                .Where(permission => permission.Permission.Code == permissionCode)
                .Select(permission => permission.Scope))
            .Distinct()
            .ToArrayAsync(cancellationToken);

        if (scopes.Contains(PermissionScope.All)) return PermissionScope.All;
        return scopes.Contains(PermissionScope.Own) ? PermissionScope.Own : null;
    }
}
