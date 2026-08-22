using Billing.Application;
using Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shine.Domain;

namespace Billing.Infrastructure;

public sealed class FinancialRecordRepository(BillingDbContext db) : IFinancialRecordRepository
{
    public async Task<Invoice> GetOrAddInvoiceAsync(Invoice candidate, CancellationToken cancellationToken = default)
    {
        var existing = await db.Invoices.Include(x => x.Transitions).SingleOrDefaultAsync(x =>
            x.AccountId == candidate.AccountId && x.IdempotencyKey == candidate.IdempotencyKey, cancellationToken);
        if (existing is not null) return Compatible(existing, candidate);
        db.Invoices.Add(candidate);
        try { await db.SaveChangesAsync(cancellationToken); return candidate; }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return Compatible(await db.Invoices.Include(x => x.Transitions).SingleAsync(x =>
                x.AccountId == candidate.AccountId && x.IdempotencyKey == candidate.IdempotencyKey, cancellationToken), candidate);
        }
    }

    public async Task<Charge> GetOrAddChargeAsync(Charge candidate, CancellationToken cancellationToken = default)
    {
        var existing = await QueryCharges().SingleOrDefaultAsync(x =>
            x.AccountId == candidate.AccountId && x.IdempotencyKey == candidate.IdempotencyKey, cancellationToken);
        if (existing is not null) return Compatible(existing, candidate);
        db.Charges.Add(candidate);
        try { await db.SaveChangesAsync(cancellationToken); return candidate; }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return Compatible(await QueryCharges().SingleAsync(x =>
                x.AccountId == candidate.AccountId && x.IdempotencyKey == candidate.IdempotencyKey, cancellationToken), candidate);
        }
    }

    public Task<Invoice?> GetInvoiceAsync(Guid accountId, Guid invoiceId, CancellationToken cancellationToken = default) =>
        db.Invoices.Include(x => x.Transitions).SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == invoiceId, cancellationToken);
    public Task<Charge?> GetChargeAsync(Guid accountId, Guid chargeId, CancellationToken cancellationToken = default) =>
        QueryCharges().SingleOrDefaultAsync(x => x.AccountId == accountId && x.Id == chargeId, cancellationToken);
    public Task SaveAsync(CancellationToken cancellationToken = default) => db.SaveChangesAsync(cancellationToken);
    private IQueryable<Charge> QueryCharges() => db.Charges.Include(x => x.Attempts).Include(x => x.Payments).Include(x => x.Transitions);
    private static Invoice Compatible(Invoice stored, Invoice candidate) =>
        stored.SubscriptionId == candidate.SubscriptionId && stored.Amount == candidate.Amount && stored.Currency == candidate.Currency &&
        SameDatabaseInstant(stored.CycleStartsAtUtc, candidate.CycleStartsAtUtc) &&
        SameDatabaseInstant(stored.CycleEndsAtUtc, candidate.CycleEndsAtUtc) &&
        SameDatabaseInstant(stored.DueAtUtc, candidate.DueAtUtc)
            ? stored : throw new DomainException("The invoice idempotency key was reused with different data.");
    private static Charge Compatible(Charge stored, Charge candidate) =>
        stored.InvoiceId == candidate.InvoiceId && stored.SubscriptionId == candidate.SubscriptionId && stored.Amount == candidate.Amount && stored.Currency == candidate.Currency
            ? stored : throw new DomainException("The charge idempotency key was reused with different data.");
    private static bool SameDatabaseInstant(DateTime left, DateTime right) => Math.Abs(left.Ticks - right.Ticks) < 10;
}
