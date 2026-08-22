using Billing.Application;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Billing.Infrastructure;

public sealed class SubscriptionUnitOwnership(ShineDbContext coreDb) : ISubscriptionUnitOwnership
{
    public async Task<bool> AllBelongToAccountAsync(Guid accountId, IReadOnlyCollection<Guid> unitIds,
        CancellationToken cancellationToken = default)
    {
        if (accountId == Guid.Empty || unitIds.Count == 0) return false;
        var distinctUnits = unitIds.Distinct().ToArray();
        var matchingUnits = await coreDb.Tenants.AsNoTracking()
            .CountAsync(x => distinctUnits.Contains(x.Id) && x.CustomerAccountId == accountId, cancellationToken);
        return matchingUnits == distinctUnits.Length;
    }
}

public sealed class SubscriptionPlanCatalog(ShineDbContext coreDb) : ISubscriptionPlanCatalog
{
    public Task<bool> ExistsAsync(Guid planId, CancellationToken cancellationToken = default) =>
        planId == Guid.Empty
            ? Task.FromResult(false)
            : coreDb.Plans.AsNoTracking().AnyAsync(x => x.Id == planId, cancellationToken);
}
