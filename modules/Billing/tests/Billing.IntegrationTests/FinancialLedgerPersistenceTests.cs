using Billing.Application;
using Billing.Domain;
using Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class FinancialLedgerPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Financial_cycle_is_persisted_with_immutable_history_and_account_isolation()
    {
        var now = DateTime.UtcNow; var accountId = Guid.NewGuid();
        var subscription = new Subscription(accountId, Guid.NewGuid(), new BillingInterval(BillingIntervalUnit.Month, 1));
        await using var db = fixture.CreateBillingDb(); db.Subscriptions.Add(subscription); await db.SaveChangesAsync();
        var service = new FinancialLedgerService(new FinancialRecordRepository(db));

        var invoice = await service.CreateInvoiceAsync(accountId, subscription.Id, "cycle-2026-08", 149.90m, "BRL", now, now.AddMonths(1), now.AddDays(5), now);
        Assert.Equal(invoice.Id, (await service.CreateInvoiceAsync(accountId, subscription.Id, "cycle-2026-08", 149.90m, "BRL", now, now.AddMonths(1), now.AddDays(5), now)).Id);
        await Assert.ThrowsAsync<DomainException>(() => service.CreateInvoiceAsync(accountId, subscription.Id,
            "cycle-2026-08", 999m, "BRL", now, now.AddMonths(1), now.AddDays(5), now));
        var charge = await service.CreateChargeAsync(accountId, invoice.Id, "charge-cycle-2026-08", now);
        var externalReferenceSuffix = accountId.ToString("N");
        var attempt = await service.StartAttemptAsync(accountId, charge.Id, "sample", $"attempt-{externalReferenceSuffix}", now);
        var payment = await service.SucceedAttemptAsync(accountId, charge.Id, attempt.Id, $"payment-{externalReferenceSuffix}", now);

        await using var verification = fixture.CreateBillingDb();
        var storedInvoice = await verification.Invoices.Include(x => x.Transitions).SingleAsync(x => x.Id == invoice.Id);
        var storedCharge = await verification.Charges.Include(x => x.Attempts).Include(x => x.Payments).Include(x => x.Transitions).SingleAsync(x => x.Id == charge.Id);
        Assert.Equal(InvoiceStatus.Paid, storedInvoice.Status); Assert.Equal(ChargeStatus.Succeeded, storedCharge.Status);
        Assert.Equal(payment.Id, Assert.Single(storedCharge.Payments).Id);
        Assert.Equal(["INVOICE_CREATED", "INVOICE_OPENED", "INVOICE_PAID"], storedInvoice.Transitions.OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Action).Select(x => x.Action).OrderBy(x => x));
        Assert.Contains(storedCharge.Transitions, x => x.Action == "PAYMENT_SETTLED");
        Assert.Null(await new FinancialRecordRepository(verification).GetInvoiceAsync(Guid.NewGuid(), invoice.Id));
        await Assert.ThrowsAsync<DomainException>(() => new FinancialLedgerService(new FinancialRecordRepository(verification)).CreateChargeAsync(Guid.NewGuid(), invoice.Id, "foreign", now));
    }

    [Fact]
    public async Task Concurrent_invoice_creation_converges_on_one_internal_record()
    {
        var now=DateTime.UtcNow;var accountId=Guid.NewGuid();var subscription=new Subscription(accountId,Guid.NewGuid(),new BillingInterval(BillingIntervalUnit.Month,1));
        await using(var seed=fixture.CreateBillingDb()){seed.Subscriptions.Add(subscription);await seed.SaveChangesAsync();}
        async Task<Guid> Create(){await using var db=fixture.CreateBillingDb();var result=await new FinancialLedgerService(new FinancialRecordRepository(db)).CreateInvoiceAsync(accountId,subscription.Id,"concurrent-cycle",10,"BRL",now,now.AddMonths(1),now.AddDays(1),now);return result.Id;}
        var ids=await Task.WhenAll(Create(),Create()); Assert.Single(ids.Distinct());
        await using var verification=fixture.CreateBillingDb();Assert.Equal(1,await verification.Invoices.CountAsync(x=>x.AccountId==accountId&&x.IdempotencyKey=="concurrent-cycle"));
    }
}
