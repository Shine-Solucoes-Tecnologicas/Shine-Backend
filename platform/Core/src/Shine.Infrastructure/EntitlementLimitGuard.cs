using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public interface IEntitlementLimitGuard
{
    Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default);
    Task ReleaseAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default);
    Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, Guid operationId, long quantity = 1, CancellationToken cancellationToken = default);
    Task ReleaseAsync(Guid unitId, string key, Guid operationId, CancellationToken cancellationToken = default);
    Task ReconcileAsync(Guid unitId, string key, IReadOnlySet<Guid> activeOperationIds, CancellationToken cancellationToken = default);
}

public sealed class EntitlementLimitGuard(ShineDbContext db, IEntitlementAccess entitlements) : IEntitlementLimitGuard
{
    public async Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default)
    {
        Validate(unitId, quantity);
        var normalizedKey = PlanEntitlement.NormalizeKey(key);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(unitId, normalizedKey, cancellationToken);

        var usage = await db.EntitlementUsages.SingleOrDefaultAsync(x => x.TenantId == unitId && x.Key == normalizedKey, cancellationToken);
        var decision = await entitlements.EvaluateLimitAsync(unitId, normalizedKey, usage?.Used ?? 0, quantity, cancellationToken);
        if (!decision.Allowed)
        {
            await transaction.CommitAsync(cancellationToken);
            return decision;
        }

        usage ??= new EntitlementUsage(unitId, normalizedKey);
        if (db.Entry(usage).State == EntityState.Detached) db.EntitlementUsages.Add(usage);
        usage.Reserve(quantity);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return decision;
    }

    public async Task ReleaseAsync(Guid unitId, string key, long quantity = 1, CancellationToken cancellationToken = default)
    {
        Validate(unitId, quantity);
        var normalizedKey = PlanEntitlement.NormalizeKey(key);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(unitId, normalizedKey, cancellationToken);
        var usage = await db.EntitlementUsages.SingleOrDefaultAsync(x => x.TenantId == unitId && x.Key == normalizedKey, cancellationToken);
        if (usage is not null)
        {
            usage.Release(quantity);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<EntitlementLimitDecision> TryReserveAsync(Guid unitId, string key, Guid operationId, long quantity = 1, CancellationToken cancellationToken = default)
    {
        Validate(unitId, quantity);
        if (operationId == Guid.Empty) throw new ArgumentException("Operation is required.", nameof(operationId));
        var normalizedKey = PlanEntitlement.NormalizeKey(key);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(unitId, normalizedKey, cancellationToken);
        var reservation = await db.EntitlementReservations.SingleOrDefaultAsync(x => x.TenantId == unitId && x.Key == normalizedKey && x.OperationId == operationId, cancellationToken);
        if (reservation?.Status == EntitlementReservationStatus.Reserved)
        {
            await transaction.CommitAsync(cancellationToken);
            return new(normalizedKey, EntitlementLimitStatus.Available, 0, quantity, null, null);
        }
        var usage = await db.EntitlementUsages.SingleOrDefaultAsync(x => x.TenantId == unitId && x.Key == normalizedKey, cancellationToken);
        var decision = await entitlements.EvaluateLimitAsync(unitId, normalizedKey, usage?.Used ?? 0, quantity, cancellationToken);
        if (!decision.Allowed)
        {
            await transaction.CommitAsync(cancellationToken);
            return decision;
        }
        usage ??= new EntitlementUsage(unitId, normalizedKey);
        if (db.Entry(usage).State == EntityState.Detached) db.EntitlementUsages.Add(usage);
        usage.Reserve(quantity);
        if (reservation is null) db.EntitlementReservations.Add(new EntitlementReservation(unitId, normalizedKey, operationId, quantity));
        else reservation.ReserveAgain();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return decision;
    }

    public async Task ReleaseAsync(Guid unitId, string key, Guid operationId, CancellationToken cancellationToken = default)
    {
        var normalizedKey = PlanEntitlement.NormalizeKey(key);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(unitId, normalizedKey, cancellationToken);
        var reservation = await db.EntitlementReservations.SingleOrDefaultAsync(x => x.TenantId == unitId && x.Key == normalizedKey && x.OperationId == operationId, cancellationToken);
        if (reservation is not null && reservation.Status == EntitlementReservationStatus.Reserved)
        {
            var usage = await db.EntitlementUsages.SingleOrDefaultAsync(x => x.TenantId == unitId && x.Key == normalizedKey, cancellationToken);
            usage?.Release(reservation.Quantity);
            reservation.Release(DateTime.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task ReconcileAsync(Guid unitId, string key, IReadOnlySet<Guid> activeOperationIds, CancellationToken cancellationToken = default)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("Unit is required.", nameof(unitId));
        var normalizedKey = PlanEntitlement.NormalizeKey(key);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await LockAsync(unitId, normalizedKey, cancellationToken);

        var reservations = await db.EntitlementReservations
            .Where(x => x.TenantId == unitId && x.Key == normalizedKey)
            .ToDictionaryAsync(x => x.OperationId, cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var reservation in reservations.Values.Where(x => x.Status == EntitlementReservationStatus.Reserved && !activeOperationIds.Contains(x.OperationId)))
            reservation.Release(now);

        foreach (var operationId in activeOperationIds)
        {
            if (!reservations.TryGetValue(operationId, out var reservation))
            {
                reservation = new EntitlementReservation(unitId, normalizedKey, operationId, 1);
                reservations.Add(operationId, reservation);
                db.EntitlementReservations.Add(reservation);
            }
            else if (reservation.Status == EntitlementReservationStatus.Released)
            {
                reservation.ReserveAgain();
            }
        }

        var authoritativeUsage = reservations.Values
            .Where(x => x.Status == EntitlementReservationStatus.Reserved)
            .Sum(x => x.Quantity);
        var usage = await db.EntitlementUsages.SingleOrDefaultAsync(x => x.TenantId == unitId && x.Key == normalizedKey, cancellationToken);
        if (usage is null)
        {
            usage = new EntitlementUsage(unitId, normalizedKey);
            db.EntitlementUsages.Add(usage);
        }
        usage.Reconcile(authoritativeUsage);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private Task LockAsync(Guid unitId, string key, CancellationToken cancellationToken)
    {
        var lockKey = $"entitlement:{unitId:N}:{key}";
        return db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);
    }

    private static void Validate(Guid unitId, long quantity)
    {
        if (unitId == Guid.Empty) throw new ArgumentException("Unit is required.", nameof(unitId));
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity));
    }
}
