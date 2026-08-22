using Billing.Domain;
using Shine.Domain;

namespace Billing.UnitTests;

public sealed class FinancialRecordTests
{
    private static readonly DateTime Now = new(2026, 8, 21, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Invoice_transitions_are_validated_idempotent_and_recorded()
    {
        var invoice = Invoice();
        Assert.True(invoice.Open(Now));
        Assert.False(invoice.Open(Now));
        Assert.True(invoice.MarkPaid(Now));
        Assert.False(invoice.MarkPaid(Now));
        Assert.Equal(["INVOICE_CREATED", "INVOICE_OPENED", "INVOICE_PAID"], invoice.Transitions.Select(x => x.Action));
        Assert.Throws<DomainException>(() => invoice.Void(Now));
    }

    [Fact]
    public void Charge_reuses_external_attempt_and_payment_identifiers_without_duplicate_effects()
    {
        var charge = new Charge(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "charge-1", 99.90m, "brl", Now);
        var attempt = charge.StartAttempt("sample", "attempt-1", Now);
        Assert.Same(attempt, charge.StartAttempt("sample", "attempt-1", Now));
        var payment = charge.SucceedAttempt(attempt.Id, "payment-1", Now);
        Assert.Same(payment, charge.SucceedAttempt(attempt.Id, "payment-1", Now));
        Assert.Single(charge.Attempts); Assert.Single(charge.Payments);
        Assert.Equal(ChargeStatus.Succeeded, charge.Status);
    }

    [Fact]
    public void Invalid_amount_currency_and_state_are_rejected()
    {
        Assert.Throws<DomainException>(() => new Invoice(Guid.NewGuid(), Guid.NewGuid(), "invoice", 0, "BRL", Now, Now.AddMonths(1), Now.AddDays(1), Now));
        Assert.Throws<DomainException>(() => new Invoice(Guid.NewGuid(), Guid.NewGuid(), "invoice", 10, "REAL", Now, Now.AddMonths(1), Now.AddDays(1), Now));
        Assert.Throws<DomainException>(() => Invoice().MarkPaid(Now));
    }

    private static Invoice Invoice() => new(Guid.NewGuid(), Guid.NewGuid(), "invoice-1", 99.90m, "BRL", Now, Now.AddMonths(1), Now.AddDays(5), Now);
}
