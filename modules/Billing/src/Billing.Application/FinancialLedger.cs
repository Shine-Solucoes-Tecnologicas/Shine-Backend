using Billing.Domain;
using Shine.Domain;

namespace Billing.Application;

public interface IFinancialRecordRepository
{
    Task<Invoice> GetOrAddInvoiceAsync(Invoice candidate, CancellationToken cancellationToken = default);
    Task<Charge> GetOrAddChargeAsync(Charge candidate, CancellationToken cancellationToken = default);
    Task<Invoice?> GetInvoiceAsync(Guid accountId, Guid invoiceId, CancellationToken cancellationToken = default);
    Task<Charge?> GetChargeAsync(Guid accountId, Guid chargeId, CancellationToken cancellationToken = default);
    Task SaveAsync(CancellationToken cancellationToken = default);
}

public sealed class FinancialLedgerService(IFinancialRecordRepository records)
{
    public Task<Invoice> CreateInvoiceAsync(Guid accountId, Guid subscriptionId, string idempotencyKey,
        decimal amount, string currency, DateTime cycleStartsAtUtc, DateTime cycleEndsAtUtc, DateTime dueAtUtc,
        DateTime utcNow, CancellationToken cancellationToken = default) =>
        records.GetOrAddInvoiceAsync(new Invoice(accountId, subscriptionId, idempotencyKey, amount, currency,
            cycleStartsAtUtc, cycleEndsAtUtc, dueAtUtc, utcNow), cancellationToken);

    public async Task<Charge> CreateChargeAsync(Guid accountId, Guid invoiceId, string idempotencyKey,
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var invoice = await records.GetInvoiceAsync(accountId, invoiceId, cancellationToken)
            ?? throw new DomainException("Invoice was not found in the organization.");
        if (invoice.Status == InvoiceStatus.Draft) invoice.Open(utcNow);
        if (invoice.Status != InvoiceStatus.Open) throw new DomainException("Only an open invoice can be charged.");
        var charge = await records.GetOrAddChargeAsync(new Charge(accountId, invoice.SubscriptionId, invoice.Id,
            idempotencyKey, invoice.Amount, invoice.Currency, utcNow), cancellationToken);
        await records.SaveAsync(cancellationToken);
        return charge;
    }

    public async Task<PaymentAttempt> StartAttemptAsync(Guid accountId, Guid chargeId, string providerCode,
        string externalAttemptId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var charge = await RequiredCharge(accountId, chargeId, cancellationToken);
        var attempt = charge.StartAttempt(providerCode, externalAttemptId, utcNow);
        await records.SaveAsync(cancellationToken); return attempt;
    }

    public async Task FailAttemptAsync(Guid accountId, Guid chargeId, Guid attemptId, string? failureCode,
        DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var charge = await RequiredCharge(accountId, chargeId, cancellationToken);
        charge.FailAttempt(attemptId, failureCode, utcNow); await records.SaveAsync(cancellationToken);
    }

    public async Task<Payment> SucceedAttemptAsync(Guid accountId, Guid chargeId, Guid attemptId,
        string externalPaymentId, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var charge = await RequiredCharge(accountId, chargeId, cancellationToken);
        var invoice = await records.GetInvoiceAsync(accountId, charge.InvoiceId, cancellationToken)
            ?? throw new DomainException("Invoice was not found in the organization.");
        var payment = charge.SucceedAttempt(attemptId, externalPaymentId, utcNow);
        invoice.MarkPaid(utcNow); await records.SaveAsync(cancellationToken); return payment;
    }

    private async Task<Charge> RequiredCharge(Guid accountId, Guid chargeId, CancellationToken cancellationToken) =>
        await records.GetChargeAsync(accountId, chargeId, cancellationToken)
        ?? throw new DomainException("Charge was not found in the organization.");
}
