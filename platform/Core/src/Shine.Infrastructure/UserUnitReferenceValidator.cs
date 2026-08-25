using Microsoft.EntityFrameworkCore;
using Shine.Application;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public sealed class UserUnitReferenceValidator(ShineDbContext db) : IUserUnitReferenceValidator
{
    public Task<bool> IsActiveInUnitAsync(Guid unitId, Guid userId, CancellationToken cancellationToken = default) =>
        unitId != Guid.Empty && userId != Guid.Empty
            ? db.UserTenants.AsNoTracking().AnyAsync(
                membership => membership.TenantId == unitId && membership.UserId == userId && membership.IsActive,
                cancellationToken)
            : Task.FromResult(false);
}
