using Billing.Application;

namespace Shine.UnitTests;

public sealed class BillingWebhookProcessorTests
{
    private static readonly DateTime Now = new(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Duplicate_webhook_is_acknowledged_without_processing_twice()
    {
        var inbox = new MemoryInbox();
        var handler = new TestHandler();
        var processor = Processor(inbox, handler);
        var request = Request();

        Assert.Equal(ProviderWebhookResult.Processed, await processor.ProcessAsync(request));
        Assert.Equal(ProviderWebhookResult.Duplicate, await processor.ProcessAsync(request));

        Assert.Equal(1, handler.Calls);
        Assert.Equal(ProviderWebhookStatus.Processed, inbox.Status);
        Assert.Equal(64, inbox.PayloadHash?.Length);
    }

    [Fact]
    public async Task Transient_failure_is_scheduled_for_retry_and_can_be_reconciled()
    {
        var inbox = new MemoryInbox();
        var handler = new TestHandler { Failure = new TransientBillingProviderException("temporary outage") };
        var clock = new TestClock();
        var processor = Processor(inbox, handler, clock);

        Assert.Equal(ProviderWebhookResult.RetryScheduled, await processor.ProcessAsync(Request()));
        Assert.Equal(ProviderWebhookStatus.RetryScheduled, inbox.Status);
        Assert.Equal(Now.AddMinutes(1), inbox.RetryAtUtc);

        handler.Failure = null;
        clock.UtcNow = Now.AddMinutes(2);
        Assert.Equal(1, await processor.RetryDueAsync());
        Assert.Equal(ProviderWebhookStatus.Processed, inbox.Status);
        Assert.Equal(2, handler.Calls);
    }

    [Fact]
    public async Task Sensitive_payment_fields_are_rejected_before_persistence()
    {
        var inbox = new MemoryInbox();
        var notification = Event(new Dictionary<string, string> { ["cardToken"] = "must-not-be-stored" });
        var processor = new BillingWebhookProcessor([new TestAdapter(notification)], inbox, [new TestHandler()], new TestClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(Request()));

        Assert.Null(inbox.Stored);
    }

    [Theory]
    [InlineData("4111111111111111")]
    [InlineData("{\"cvv\":\"123\"}")]
    [InlineData("{\"card_token\":\"tok_sensitive_value\"}")]
    [InlineData("tok_sensitive_value")]
    [InlineData("Bearer eyJhbGciOiJIUzI1NiJ9.payload.signature")]
    public async Task Sensitive_payment_values_inside_generic_fields_are_rejected_before_persistence(string value)
    {
        var inbox = new MemoryInbox();
        var notification = Event(new Dictionary<string, string> { ["metadata"] = value });
        var processor = new BillingWebhookProcessor([new TestAdapter(notification)], inbox, [new TestHandler()], new TestClock());

        await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(Request()));

        Assert.Null(inbox.Stored);
    }

    [Fact]
    public void Ordinary_business_fields_are_not_mistaken_for_payment_credentials()
    {
        BillingWebhookDataGuard.EnsureSafe(new Dictionary<string, string> { ["companyName"] = "Shine" });
    }

    [Fact]
    public async Task Reconciliation_reads_the_provider_and_applies_its_observed_state()
    {
        var state = new ProviderSubscriptionState("sub-123", "active", Now);
        var provider = new TestProvider(state);
        var target = new TestReconciliationTarget();
        var service = new BillingReconciliationService([provider], target);

        await service.ReconcileAsync("sample", state.ExternalSubscriptionId);

        Assert.Equal(state, target.State);
        Assert.Equal("SAMPLE", target.ProviderCode);
    }

    private static BillingWebhookProcessor Processor(MemoryInbox inbox, TestHandler handler, TestClock? clock = null) =>
        new([new TestAdapter(Event())], inbox, [handler], clock ?? new TestClock());

    private static ProviderWebhookRequest Request() => new("sample", new Dictionary<string, string>(), "signed-payload"u8.ToArray());

    private static NormalizedProviderEvent Event(IReadOnlyDictionary<string, string>? data = null) =>
        new("SAMPLE", "evt-123", "subscription.updated", "sub-123", Now, data ?? new Dictionary<string, string> { ["status"] = "active" });

    private sealed class TestAdapter(NormalizedProviderEvent notification) : IProviderWebhookAdapter
    {
        public string ProviderCode => "SAMPLE";
        public Task<NormalizedProviderEvent> ValidateAndNormalizeAsync(ProviderWebhookRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(notification);
    }

    private sealed class TestHandler : IProviderEventHandler
    {
        public int Calls { get; private set; }
        public Exception? Failure { get; set; }
        public Task HandleAsync(NormalizedProviderEvent notification, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }

    private sealed class MemoryInbox : IProviderWebhookInbox
    {
        public NormalizedProviderEvent? Stored { get; private set; }
        public string? PayloadHash { get; private set; }
        public ProviderWebhookStatus Status { get; private set; }
        public DateTime? RetryAtUtc { get; private set; }
        public DateTime? LockedUntilUtc { get; private set; }
        public Guid? ProcessingId { get; private set; }

        public Task<bool> TryStoreAsync(NormalizedProviderEvent notification, string payloadHash, CancellationToken cancellationToken = default)
        {
            if (Stored is not null) return Task.FromResult(false);
            Stored = notification;
            PayloadHash = payloadHash;
            Status = ProviderWebhookStatus.Pending;
            return Task.FromResult(true);
        }
        public Task<ClaimedProviderEvent?> ClaimAsync(string providerCode, string externalEventId, DateTime utcNow, TimeSpan lease, CancellationToken cancellationToken = default)
        {
            if (Stored is null || Status != ProviderWebhookStatus.Pending) return Task.FromResult<ClaimedProviderEvent?>(null);
            ProcessingId = Guid.NewGuid(); LockedUntilUtc = utcNow.Add(lease); Status = ProviderWebhookStatus.Processing;
            return Task.FromResult<ClaimedProviderEvent?>(new(Stored, ProcessingId.Value));
        }
        public Task<IReadOnlyCollection<ClaimedProviderEvent>> ClaimDueAsync(DateTime utcNow, DateTime pendingBeforeUtc, TimeSpan lease, int limit = 50, CancellationToken cancellationToken = default)
        {
            var due = Stored is not null && (Status == ProviderWebhookStatus.RetryScheduled && RetryAtUtc <= utcNow ||
                Status == ProviderWebhookStatus.Pending || Status == ProviderWebhookStatus.Processing && LockedUntilUtc <= utcNow);
            if (!due) return Task.FromResult<IReadOnlyCollection<ClaimedProviderEvent>>([]);
            ProcessingId = Guid.NewGuid(); LockedUntilUtc = utcNow.Add(lease); Status = ProviderWebhookStatus.Processing;
            return Task.FromResult<IReadOnlyCollection<ClaimedProviderEvent>>([new(Stored!, ProcessingId.Value)]);
        }
        public Task MarkProcessedAsync(string providerCode, string externalEventId, Guid processingId, CancellationToken cancellationToken = default)
        {
            if (ProcessingId != processingId) return Task.CompletedTask;
            Status = ProviderWebhookStatus.Processed;
            ProcessingId = null; LockedUntilUtc = null;
            return Task.CompletedTask;
        }
        public Task MarkRetryAsync(string providerCode, string externalEventId, Guid processingId, string reason, DateTime retryAtUtc, CancellationToken cancellationToken = default)
        {
            if (ProcessingId != processingId) return Task.CompletedTask;
            Status = ProviderWebhookStatus.RetryScheduled;
            RetryAtUtc = retryAtUtc;
            ProcessingId = null; LockedUntilUtc = null;
            return Task.CompletedTask;
        }
        public Task MarkFailedAsync(string providerCode, string externalEventId, Guid processingId, string reason, CancellationToken cancellationToken = default)
        {
            if (ProcessingId != processingId) return Task.CompletedTask;
            Status = ProviderWebhookStatus.Failed;
            ProcessingId = null; LockedUntilUtc = null;
            return Task.CompletedTask;
        }
    }

    private sealed class TestClock : IBillingClock { public DateTime UtcNow { get; set; } = Now; }

    private sealed class TestProvider(ProviderSubscriptionState state) : IBillingProvider
    {
        public string ProviderCode => "SAMPLE";
        public Task<ProviderSubscriptionState> GetSubscriptionAsync(string externalSubscriptionId, CancellationToken cancellationToken = default) => Task.FromResult(state);
        public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProviderChargeResult> CreateChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelAsync(string externalSubscriptionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class TestReconciliationTarget : IBillingReconciliationTarget
    {
        public string? ProviderCode { get; private set; }
        public ProviderSubscriptionState? State { get; private set; }
        public Task ApplyProviderStateAsync(string providerCode, ProviderSubscriptionState state, CancellationToken cancellationToken = default)
        {
            ProviderCode = providerCode;
            State = state;
            return Task.CompletedTask;
        }
    }
}
