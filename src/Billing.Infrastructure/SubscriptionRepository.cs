using Billing.Application;
using Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shine.Domain;

namespace Billing.Infrastructure;

public sealed class SubscriptionRepository(BillingDbContext db) : ISubscriptionRepository
{
    public Task<Subscription?> GetAsync(Guid subscriptionId, CancellationToken cancellationToken = default) =>
        db.Subscriptions.Include(x => x.Units).SingleOrDefaultAsync(x => x.Id == subscriptionId, cancellationToken);

    public Task<bool> HasEffectiveSubscriptionAsync(Guid unitId, Guid? excludingSubscriptionId = null, CancellationToken cancellationToken = default) =>
        db.SubscriptionUnits.AnyAsync(x => x.UnitId == unitId && x.IsEffective &&
            (excludingSubscriptionId == null || x.SubscriptionId != excludingSubscriptionId), cancellationToken);

    public Task AddAsync(Subscription subscription, CancellationToken cancellationToken = default)
    {
        db.Subscriptions.Add(subscription);
        return Task.CompletedTask;
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DomainException("A unit already has an effective subscription.", exception);
        }
    }

    public async Task<T> ExecuteUnitAssignmentAsync<T>(IReadOnlyCollection<Guid> unitIds,
        Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        foreach (var unitId in unitIds.Distinct().Order())
        {
            var lockKey = $"billing-subscription-unit:{unitId:N}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
        }
        var result = await operation(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}
