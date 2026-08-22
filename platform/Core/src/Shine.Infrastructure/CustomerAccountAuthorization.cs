using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface ICustomerAccountAuthorization
{
    Task<bool> HasPermissionAsync(Guid userId, Guid accountId, string permissionCode, Guid? unitId = null, string? moduleCode = null, CancellationToken cancellationToken = default);
}

public sealed class CustomerAccountAuthorization(ShineDbContext db) : ICustomerAccountAuthorization
{
    public async Task<bool> HasPermissionAsync(Guid userId, Guid accountId, string permissionCode, Guid? unitId = null, string? moduleCode = null, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || accountId == Guid.Empty || string.IsNullOrWhiteSpace(permissionCode)) return false;
        if (await db.UserGlobalRoles.AnyAsync(x => x.UserId == userId, cancellationToken)) return false;

        var normalizedModule = moduleCode is null ? null : new ModuleCode(moduleCode).Value;
        var assignments = await db.CustomerAccountUserRoles.AsNoTracking()
            .Where(x => x.AccountId == accountId && x.UserId == userId && x.Membership.IsActive && x.Role.AccountId == accountId &&
                x.Role.Permissions.Any(permission => permission.Permission.Code == permissionCode))
            .Include(x => x.Units)
            .Include(x => x.Modules)
            .ToArrayAsync(cancellationToken);

        return assignments.Any(assignment =>
            (unitId is null || assignment.AllUnits || assignment.Units.Any(x => x.UnitId == unitId.Value)) &&
            (normalizedModule is null || assignment.AllModules || assignment.Modules.Any(x => x.ModuleCode == normalizedModule)));
    }
}
