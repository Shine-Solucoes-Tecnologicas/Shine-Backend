using Shine.Domain;

namespace Billing.Domain;

public enum InvoiceStatus { Draft, Open, Paid, Voided }
public enum ChargeStatus { Pending, Processing, Succeeded, Failed, Canceled }
public enum PaymentAttemptStatus { Processing, Succeeded, Failed }

public sealed class Invoice : BaseEntity<Guid>
{
    private readonly List<FinancialTransition> transitions = [];
    private Invoice() : base(Guid.Empty) { }
    public Invoice(Guid accountId, Guid subscriptionId, string idempotencyKey, decimal amount, string currency,
        DateTime cycleStartsAtUtc, DateTime cycleEndsAtUtc, DateTime dueAtUtc, DateTime utcNow) : base(Guid.NewGuid())
    {
        AccountId = Required(accountId, "Organization"); SubscriptionId = Required(subscriptionId, "Subscription");
        IdempotencyKey = Text(idempotencyKey, "Idempotency key", 120); Amount = Positive(amount);
        Currency = CurrencyCode(currency); EnsurePeriod(cycleStartsAtUtc, cycleEndsAtUtc, dueAtUtc, utcNow);
        CycleStartsAtUtc = cycleStartsAtUtc; CycleEndsAtUtc = cycleEndsAtUtc; DueAtUtc = dueAtUtc;
        Status = InvoiceStatus.Draft; CreatedAtUtc = utcNow; AddTransition("INVOICE_CREATED", utcNow);
    }
    public Guid AccountId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!;
    public DateTime CycleStartsAtUtc { get; private set; }
    public DateTime CycleEndsAtUtc { get; private set; }
    public DateTime DueAtUtc { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? OpenedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public IReadOnlyCollection<FinancialTransition> Transitions => transitions.AsReadOnly();
    public bool Open(DateTime utcNow) { EnsureUtc(utcNow); if (Status == InvoiceStatus.Open) return false; if (Status != InvoiceStatus.Draft) Invalid(); Status = InvoiceStatus.Open; OpenedAtUtc = utcNow; AddTransition("INVOICE_OPENED", utcNow); return true; }
    public bool MarkPaid(DateTime utcNow) { EnsureUtc(utcNow); if (Status == InvoiceStatus.Paid) return false; if (Status != InvoiceStatus.Open) Invalid(); Status = InvoiceStatus.Paid; PaidAtUtc = utcNow; AddTransition("INVOICE_PAID", utcNow); return true; }
    public bool Void(DateTime utcNow) { EnsureUtc(utcNow); if (Status == InvoiceStatus.Voided) return false; if (Status is InvoiceStatus.Paid or InvoiceStatus.Voided) Invalid(); Status = InvoiceStatus.Voided; VoidedAtUtc = utcNow; AddTransition("INVOICE_VOIDED", utcNow); return true; }
    private void AddTransition(string action, DateTime utcNow) => transitions.Add(new FinancialTransition(AccountId, SubscriptionId, "INVOICE", Id, action, utcNow));
    private static void Invalid() => throw new DomainException("The invoice transition is invalid.");
    internal static Guid Required(Guid value, string name) => value == Guid.Empty ? throw new DomainException($"{name} is required.") : value;
    internal static string Text(string value, string name, int max) => string.IsNullOrWhiteSpace(value) || value.Trim().Length > max ? throw new DomainException($"{name} is invalid.") : value.Trim();
    internal static decimal Positive(decimal value) => value <= 0 ? throw new DomainException("Amount must be positive.") : decimal.Round(value, 2, MidpointRounding.ToEven);
    internal static string CurrencyCode(string value) { var code = Text(value, "Currency", 3).ToUpperInvariant(); return code.Length == 3 && code.All(char.IsLetter) ? code : throw new DomainException("Currency is invalid."); }
    internal static void EnsureUtc(DateTime value) { if (value.Kind != DateTimeKind.Utc) throw new DomainException("Billing dates must use UTC."); }
    private static void EnsurePeriod(DateTime start, DateTime end, DateTime due, DateTime now) { EnsureUtc(start); EnsureUtc(end); EnsureUtc(due); EnsureUtc(now); if (start >= end || due < start) throw new DomainException("Invoice period is invalid."); }
}

public sealed class Charge : BaseEntity<Guid>
{
    private readonly List<PaymentAttempt> attempts = [];
    private readonly List<Payment> payments = [];
    private readonly List<FinancialTransition> transitions = [];
    private Charge() : base(Guid.Empty) { }
    public Charge(Guid accountId, Guid subscriptionId, Guid invoiceId, string idempotencyKey, decimal amount, string currency, DateTime utcNow) : base(Guid.NewGuid())
    {
        AccountId=Invoice.Required(accountId,"Organization"); SubscriptionId=Invoice.Required(subscriptionId,"Subscription"); InvoiceId=Invoice.Required(invoiceId,"Invoice");
        IdempotencyKey=Invoice.Text(idempotencyKey,"Idempotency key",120); Amount=Invoice.Positive(amount); Currency=Invoice.CurrencyCode(currency); Invoice.EnsureUtc(utcNow);
        Status=ChargeStatus.Pending; CreatedAtUtc=utcNow; AddTransition("CHARGE_CREATED", utcNow);
    }
    public Guid AccountId { get; private set; } public Guid SubscriptionId { get; private set; } public Guid InvoiceId { get; private set; }
    public string IdempotencyKey { get; private set; }=null!; public decimal Amount { get; private set; } public string Currency { get; private set; }=null!;
    public ChargeStatus Status { get; private set; } public DateTime CreatedAtUtc { get; private set; } public DateTime? CompletedAtUtc { get; private set; }
    public IReadOnlyCollection<PaymentAttempt> Attempts=>attempts.AsReadOnly(); public IReadOnlyCollection<Payment> Payments=>payments.AsReadOnly(); public IReadOnlyCollection<FinancialTransition> Transitions=>transitions.AsReadOnly();
    public PaymentAttempt StartAttempt(string providerCode, string externalAttemptId, DateTime utcNow)
    {
        Invoice.EnsureUtc(utcNow); var provider=Invoice.Text(providerCode,"Provider",80).ToUpperInvariant(); var external=Invoice.Text(externalAttemptId,"External attempt",200);
        var existing=attempts.SingleOrDefault(x=>x.ProviderCode==provider&&x.ExternalAttemptId==external); if(existing is not null)return existing;
        if(Status is ChargeStatus.Succeeded or ChargeStatus.Canceled)throw new DomainException("The charge cannot receive another attempt.");
        Status=ChargeStatus.Processing; var attempt=new PaymentAttempt(AccountId,SubscriptionId,Id,provider,external,utcNow); attempts.Add(attempt); AddTransition("ATTEMPT_STARTED",utcNow,attempt.Id); return attempt;
    }
    public bool FailAttempt(Guid attemptId,string? failureCode,DateTime utcNow){var attempt=Attempt(attemptId);if(!attempt.Fail(failureCode,utcNow))return false;Status=ChargeStatus.Failed;CompletedAtUtc=utcNow;AddTransition("ATTEMPT_FAILED",utcNow,attempt.Id);return true;}
    public Payment SucceedAttempt(Guid attemptId,string externalPaymentId,DateTime utcNow)
    {
        var attempt=Attempt(attemptId); var external=Invoice.Text(externalPaymentId,"External payment",200); var existing=payments.SingleOrDefault(x=>x.ProviderCode==attempt.ProviderCode&&x.ExternalPaymentId==external);if(existing is not null)return existing;
        attempt.Succeed(utcNow); Status=ChargeStatus.Succeeded;CompletedAtUtc=utcNow;var payment=new Payment(AccountId,SubscriptionId,InvoiceId,Id,attempt.ProviderCode,external,Amount,Currency,utcNow);payments.Add(payment);AddTransition("PAYMENT_SETTLED",utcNow,payment.Id);return payment;
    }
    public bool Cancel(DateTime utcNow){Invoice.EnsureUtc(utcNow);if(Status==ChargeStatus.Canceled)return false;if(Status==ChargeStatus.Succeeded)throw new DomainException("A succeeded charge cannot be canceled.");Status=ChargeStatus.Canceled;CompletedAtUtc=utcNow;AddTransition("CHARGE_CANCELED",utcNow);return true;}
    private PaymentAttempt Attempt(Guid id)=>attempts.SingleOrDefault(x=>x.Id==id)??throw new DomainException("Payment attempt was not found in the charge.");
    private void AddTransition(string action,DateTime utcNow,Guid? relatedId=null)=>transitions.Add(new FinancialTransition(AccountId,SubscriptionId,"CHARGE",Id,action,utcNow,relatedId));
}

public sealed class PaymentAttempt : BaseEntity<Guid>
{
    private PaymentAttempt():base(Guid.Empty){} internal PaymentAttempt(Guid accountId,Guid subscriptionId,Guid chargeId,string provider,string external,DateTime utcNow):base(Guid.NewGuid()){AccountId=accountId;SubscriptionId=subscriptionId;ChargeId=chargeId;ProviderCode=provider;ExternalAttemptId=external;Status=PaymentAttemptStatus.Processing;StartedAtUtc=utcNow;}
    public Guid AccountId{get;private set;} public Guid SubscriptionId{get;private set;} public Guid ChargeId{get;private set;} public string ProviderCode{get;private set;}=null!; public string ExternalAttemptId{get;private set;}=null!; public PaymentAttemptStatus Status{get;private set;} public string? FailureCode{get;private set;} public DateTime StartedAtUtc{get;private set;} public DateTime? CompletedAtUtc{get;private set;}
    internal bool Fail(string? code,DateTime utcNow){Invoice.EnsureUtc(utcNow);if(Status==PaymentAttemptStatus.Failed)return false;if(Status!=PaymentAttemptStatus.Processing)throw new DomainException("The payment attempt transition is invalid.");Status=PaymentAttemptStatus.Failed;FailureCode=string.IsNullOrWhiteSpace(code)?null:Invoice.Text(code,"Failure code",120);CompletedAtUtc=utcNow;return true;}
    internal bool Succeed(DateTime utcNow){Invoice.EnsureUtc(utcNow);if(Status==PaymentAttemptStatus.Succeeded)return false;if(Status!=PaymentAttemptStatus.Processing)throw new DomainException("The payment attempt transition is invalid.");Status=PaymentAttemptStatus.Succeeded;CompletedAtUtc=utcNow;return true;}
}

public sealed class Payment : BaseEntity<Guid>
{
    private Payment():base(Guid.Empty){} internal Payment(Guid accountId,Guid subscriptionId,Guid invoiceId,Guid chargeId,string provider,string external,decimal amount,string currency,DateTime utcNow):base(Guid.NewGuid()){AccountId=accountId;SubscriptionId=subscriptionId;InvoiceId=invoiceId;ChargeId=chargeId;ProviderCode=provider;ExternalPaymentId=external;Amount=amount;Currency=currency;SettledAtUtc=utcNow;}
    public Guid AccountId{get;private set;} public Guid SubscriptionId{get;private set;} public Guid InvoiceId{get;private set;} public Guid ChargeId{get;private set;} public string ProviderCode{get;private set;}=null!; public string ExternalPaymentId{get;private set;}=null!; public decimal Amount{get;private set;} public string Currency{get;private set;}=null!; public DateTime SettledAtUtc{get;private set;}
}

public sealed class FinancialTransition : BaseEntity<Guid>
{
    private FinancialTransition():base(Guid.Empty){} internal FinancialTransition(Guid accountId,Guid subscriptionId,string entityType,Guid entityId,string action,DateTime occurredAtUtc,Guid? relatedId=null):base(Guid.NewGuid()){AccountId=accountId;SubscriptionId=subscriptionId;EntityType=entityType;EntityId=entityId;InvoiceId=entityType=="INVOICE"?entityId:null;ChargeId=entityType=="CHARGE"?entityId:null;Action=action;OccurredAtUtc=occurredAtUtc;RelatedEntityId=relatedId;}
    public Guid AccountId{get;private set;} public Guid SubscriptionId{get;private set;} public string EntityType{get;private set;}=null!; public Guid EntityId{get;private set;} public Guid? InvoiceId{get;private set;} public Guid? ChargeId{get;private set;} public string Action{get;private set;}=null!; public DateTime OccurredAtUtc{get;private set;} public Guid? RelatedEntityId{get;private set;}
}
