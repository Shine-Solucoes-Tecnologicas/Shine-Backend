using Microsoft.EntityFrameworkCore;
using Billing.Application;
using Billing.Domain;
using System.Text.Json;

namespace Billing.Infrastructure;

public sealed class BillingDbContext(DbContextOptions<BillingDbContext> options) : DbContext(options)
{
    public DbSet<ProcessedBillingEvent> ProcessedEvents => Set<ProcessedBillingEvent>();
    public DbSet<ProviderWebhookInboxItem> ProviderWebhookInbox => Set<ProviderWebhookInboxItem>();
    public DbSet<CommercialContract> CommercialContracts => Set<CommercialContract>();
    public DbSet<CommercialContractRevision> CommercialContractRevisions => Set<CommercialContractRevision>();
    public DbSet<CommercialTerm> CommercialTerms => Set<CommercialTerm>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<SubscriptionUnit> SubscriptionUnits => Set<SubscriptionUnit>();
    public DbSet<BillingOutboxMessage> OutboxMessages => Set<BillingOutboxMessage>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<Charge> Charges => Set<Charge>();
    public DbSet<PaymentAttempt> PaymentAttempts => Set<PaymentAttempt>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<FinancialTransition> FinancialTransitions => Set<FinancialTransition>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var aggregates = CaptureOutboxMessages();
        var result = base.SaveChanges(acceptAllChangesOnSuccess);
        foreach (var aggregate in aggregates) aggregate.ClearDomainEvents();
        return result;
    }

    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var aggregates = CaptureOutboxMessages();
        var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        foreach (var aggregate in aggregates) aggregate.ClearDomainEvents();
        return result;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProcessedBillingEvent>(entity =>
        {
            entity.ToTable("BillingProcessedEvents");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.ProcessedAtUtc);
        });
        modelBuilder.Entity<ProviderWebhookInboxItem>(entity =>
        {
            entity.ToTable("BillingProviderWebhookInbox");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ProviderCode).HasMaxLength(80).IsRequired();
            entity.Property(x => x.ExternalEventId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.EventType).HasMaxLength(160).IsRequired();
            entity.Property(x => x.ExternalSubscriptionId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PayloadHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.DataJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.Property(x => x.Status).HasConversion<int>();
            entity.HasIndex(x => new { x.ProviderCode, x.ExternalEventId }).IsUnique();
            entity.HasIndex(x => new { x.Status, x.NextRetryAtUtc });
            entity.HasIndex(x => new { x.Status, x.ReceivedAtUtc });
            entity.HasIndex(x => x.LockedUntilUtc);
        });
        modelBuilder.Entity<CommercialContract>(entity =>
        {
            entity.ToTable("BillingCommercialContracts");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Reference).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.CurrentRevisionNumber);
            entity.HasIndex(x => new { x.AccountId, x.Reference }).IsUnique();
            entity.HasOne<Subscription>().WithMany()
                .HasForeignKey(x => new { x.SubscriptionId, x.AccountId })
                .HasPrincipalKey(x => new { x.Id, x.AccountId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Revisions).WithOne(x => x.Contract).HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<CommercialContractRevision>(entity =>
        {
            entity.ToTable("BillingCommercialContractRevisions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Justification).HasMaxLength(1000).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.HasIndex(x => new { x.ContractId, x.RevisionNumber }).IsUnique();
            entity.HasMany(x => x.Terms).WithOne(x => x.Revision).HasForeignKey(x => x.RevisionId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Terms).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<CommercialTerm>(entity =>
        {
            entity.ToTable("BillingCommercialTerms");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Code).HasMaxLength(120).IsRequired();
            entity.Property(x => x.ValueType).HasMaxLength(40).IsRequired();
            entity.Property(x => x.Value).HasMaxLength(2000).IsRequired();
            entity.Property(x => x.ParametersJson).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(x => new { x.RevisionId, x.Category, x.Code }).IsUnique();
        });
        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.ToTable("BillingSubscriptions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.Interval).HasConversion(
                value => $"{(int)value.Unit}:{value.Count}",
                value => ParseInterval(value)).HasMaxLength(30).IsRequired();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.CheckoutIdempotencyKey).HasMaxLength(120);
            entity.Property(x => x.ProviderCode).HasMaxLength(80);
            entity.Property(x => x.ExternalSubscriptionId).HasMaxLength(200);
            entity.HasIndex(x => x.AccountId);
            entity.HasIndex(x => new { x.AccountId, x.CheckoutIdempotencyKey }).IsUnique()
                .HasFilter("\"CheckoutIdempotencyKey\" IS NOT NULL");
            entity.HasIndex(x => new { x.ProviderCode, x.ExternalSubscriptionId }).IsUnique()
                .HasFilter("\"ExternalSubscriptionId\" IS NOT NULL");
            entity.HasAlternateKey(x => new { x.Id, x.AccountId });
            entity.HasMany(x => x.Units).WithOne(x => x.Subscription).HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Units).UsePropertyAccessMode(PropertyAccessMode.Field);
            entity.Ignore(x => x.UnitIds);
            entity.Ignore(x => x.DomainEvents);
        });
        modelBuilder.Entity<SubscriptionUnit>(entity =>
        {
            entity.ToTable("BillingSubscriptionUnits");
            entity.HasKey(x => new { x.SubscriptionId, x.UnitId });
            entity.HasIndex(x => x.UnitId).IsUnique().HasFilter("\"IsEffective\" = TRUE");
        });
        modelBuilder.Entity<BillingOutboxMessage>(entity =>
        {
            entity.ToTable("BillingOutbox");
            entity.HasKey(x => x.EventId);
            entity.Property(x => x.EventType).HasMaxLength(200).IsRequired();
            entity.Property(x => x.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasConversion<int>();
            entity.Property(x => x.LastError).HasMaxLength(2000);
            entity.HasIndex(x => new { x.Status, x.NextAttemptAtUtc });
            entity.HasIndex(x => x.LockedUntilUtc);
        });
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.ToTable("BillingInvoices"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(120).IsRequired(); entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired(); entity.Property(x => x.Status).HasConversion<int>();
            entity.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique(); entity.HasIndex(x => new { x.AccountId, x.DueAtUtc });
            entity.HasAlternateKey(x => new { x.Id, x.AccountId, x.SubscriptionId });
            entity.HasOne<Subscription>().WithMany().HasForeignKey(x => new { x.SubscriptionId, x.AccountId })
                .HasPrincipalKey(x => new { x.Id, x.AccountId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Transitions).WithOne().HasForeignKey(x => x.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Transitions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<Charge>(entity =>
        {
            entity.ToTable("BillingCharges"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.IdempotencyKey).HasMaxLength(120).IsRequired(); entity.Property(x => x.Amount).HasPrecision(18, 2);
            entity.Property(x => x.Currency).HasMaxLength(3).IsRequired(); entity.Property(x => x.Status).HasConversion<int>();
            entity.HasIndex(x => new { x.AccountId, x.IdempotencyKey }).IsUnique(); entity.HasIndex(x => new { x.AccountId, x.InvoiceId });
            entity.HasAlternateKey(x => new { x.Id, x.AccountId, x.SubscriptionId });
            entity.HasOne<Invoice>().WithMany().HasForeignKey(x => new { x.InvoiceId, x.AccountId, x.SubscriptionId })
                .HasPrincipalKey(x => new { x.Id, x.AccountId, x.SubscriptionId }).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Attempts).WithOne().HasForeignKey(x => new { x.ChargeId, x.AccountId, x.SubscriptionId })
                .HasPrincipalKey(x => new { x.Id, x.AccountId, x.SubscriptionId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Payments).WithOne().HasForeignKey(x => new { x.ChargeId, x.AccountId, x.SubscriptionId })
                .HasPrincipalKey(x => new { x.Id, x.AccountId, x.SubscriptionId }).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.Transitions).WithOne().HasForeignKey(x => x.ChargeId).OnDelete(DeleteBehavior.Cascade);
            entity.Navigation(x => x.Attempts).UsePropertyAccessMode(PropertyAccessMode.Field); entity.Navigation(x => x.Payments).UsePropertyAccessMode(PropertyAccessMode.Field); entity.Navigation(x => x.Transitions).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        modelBuilder.Entity<PaymentAttempt>(entity =>
        {
            entity.ToTable("BillingPaymentAttempts"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ProviderCode).HasMaxLength(80).IsRequired(); entity.Property(x => x.ExternalAttemptId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.FailureCode).HasMaxLength(120); entity.Property(x => x.Status).HasConversion<int>();
            entity.HasIndex(x => new { x.ProviderCode, x.ExternalAttemptId }).IsUnique(); entity.HasIndex(x => new { x.AccountId, x.ChargeId });
        });
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("BillingPayments"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.ProviderCode).HasMaxLength(80).IsRequired(); entity.Property(x => x.ExternalPaymentId).HasMaxLength(200).IsRequired();
            entity.Property(x => x.Amount).HasPrecision(18, 2); entity.Property(x => x.Currency).HasMaxLength(3).IsRequired();
            entity.HasIndex(x => new { x.ProviderCode, x.ExternalPaymentId }).IsUnique(); entity.HasIndex(x => new { x.AccountId, x.InvoiceId });
        });
        modelBuilder.Entity<FinancialTransition>(entity =>
        {
            entity.ToTable("BillingFinancialTransitions"); entity.HasKey(x => x.Id); entity.Property(x => x.Id).ValueGeneratedNever();
            entity.Property(x => x.EntityType).HasMaxLength(30).IsRequired(); entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.HasIndex(x => new { x.AccountId, x.EntityType, x.EntityId, x.OccurredAtUtc });
        });
    }

    private IReadOnlyCollection<Subscription> CaptureOutboxMessages()
    {
        var aggregates = ChangeTracker.Entries<Subscription>().Select(x => x.Entity)
            .Where(x => x.DomainEvents.Count > 0).Distinct().ToArray();
        var localEventIds = OutboxMessages.Local.Select(x => x.EventId).ToHashSet();
        foreach (var domainEvent in aggregates.SelectMany(x => x.DomainEvents).OfType<ISubscriptionIntegrationEvent>())
        {
            if (localEventIds.Add(domainEvent.EventId))
                OutboxMessages.Add(BillingOutboxMessage.From(domainEvent));
        }
        return aggregates;
    }

    private static BillingInterval ParseInterval(string value)
    {
        var parts = value.Split(':', 2);
        return parts.Length == 2 && int.TryParse(parts[0], out var unit) && int.TryParse(parts[1], out var count)
            ? new BillingInterval((BillingIntervalUnit)unit, count)
            : throw new InvalidOperationException("Stored billing interval is invalid.");
    }
}

public sealed class ProviderWebhookInboxItem
{
    private ProviderWebhookInboxItem() { }
    public ProviderWebhookInboxItem(string providerCode, string externalEventId, string eventType, string externalSubscriptionId,
        DateTime occurredAtUtc, string dataJson, string payloadHash)
    {
        Id = Guid.NewGuid();
        ProviderCode = providerCode.Trim().ToUpperInvariant();
        ExternalEventId = externalEventId.Trim();
        EventType = eventType.Trim();
        ExternalSubscriptionId = externalSubscriptionId.Trim();
        OccurredAtUtc = occurredAtUtc;
        DataJson = dataJson;
        PayloadHash = payloadHash;
        Status = ProviderWebhookStatus.Pending;
        ReceivedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public string ProviderCode { get; private set; } = null!;
    public string ExternalEventId { get; private set; } = null!;
    public string EventType { get; private set; } = null!;
    public string ExternalSubscriptionId { get; private set; } = null!;
    public DateTime OccurredAtUtc { get; private set; }
    public string DataJson { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public ProviderWebhookStatus Status { get; private set; }
    public int Attempts { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public Guid? ProcessingId { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public string? LastError { get; private set; }

    public Guid Claim(DateTime utcNow, TimeSpan lease) { Status = ProviderWebhookStatus.Processing; ProcessingId = Guid.NewGuid(); LockedUntilUtc = utcNow.Add(lease); Attempts++; return ProcessingId.Value; }
    public bool IsOwnedBy(Guid processingId) => Status == ProviderWebhookStatus.Processing && ProcessingId == processingId;
    public void MarkProcessed(Guid processingId, DateTime utcNow) { EnsureOwner(processingId); Status = ProviderWebhookStatus.Processed; ProcessedAtUtc = utcNow; NextRetryAtUtc = null; LastError = null; ClearLease(); }
    public void MarkRetry(Guid processingId, string reason, DateTime retryAtUtc) { EnsureOwner(processingId); Status = ProviderWebhookStatus.RetryScheduled; NextRetryAtUtc = retryAtUtc; LastError = Truncate(reason); ClearLease(); }
    public void MarkFailed(Guid processingId, string reason) { EnsureOwner(processingId); Status = ProviderWebhookStatus.Failed; NextRetryAtUtc = null; LastError = Truncate(reason); ClearLease(); }
    private void EnsureOwner(Guid processingId) { if (!IsOwnedBy(processingId)) throw new InvalidOperationException("The webhook is not owned by this processor."); }
    private void ClearLease() { ProcessingId = null; LockedUntilUtc = null; }
    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}

public sealed class ProcessedBillingEvent
{
    private ProcessedBillingEvent() { }
    public ProcessedBillingEvent(Guid eventId, string eventType, string correlationId, DateTime processedAtUtc)
    {
        if (eventId == Guid.Empty) throw new ArgumentException("Event is required.", nameof(eventId));
        EventId = eventId;
        EventType = eventType;
        CorrelationId = correlationId;
        ProcessedAtUtc = processedAtUtc;
    }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = null!;
    public string CorrelationId { get; private set; } = null!;
    public DateTime ProcessedAtUtc { get; private set; }
}
