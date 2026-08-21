using Billing.Application;
using Billing.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class BillingWebhookInboxTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Inbox_persists_normalized_data_and_suppresses_duplicate_delivery()
    {
        await using var db = fixture.CreateBillingDb();
        var inbox = new ProviderWebhookInbox(db);
        var externalEventId = $"evt-{Guid.NewGuid():N}";
        var notification = Notification(externalEventId);

        Assert.True(await inbox.TryStoreAsync(notification, new string('A', 64)));
        Assert.False(await inbox.TryStoreAsync(notification, new string('A', 64)));

        var stored = await db.ProviderWebhookInbox.SingleAsync(x => x.ExternalEventId == externalEventId);
        Assert.Equal("SAMPLE", stored.ProviderCode);
        Assert.Contains("status", stored.DataJson);
        Assert.DoesNotContain("signed-raw-payload", stored.DataJson);
        Assert.Equal(ProviderWebhookStatus.Pending, stored.Status);
    }

    [Fact]
    public async Task Scheduled_retry_is_returned_only_when_due()
    {
        await using var db = fixture.CreateBillingDb();
        var inbox = new ProviderWebhookInbox(db);
        var externalEventId = $"evt-{Guid.NewGuid():N}";
        var notification = Notification(externalEventId);
        await inbox.TryStoreAsync(notification, new string('B', 64));
        var initialClaim = await inbox.ClaimAsync(notification.ProviderCode, externalEventId, DateTime.UtcNow, TimeSpan.FromMinutes(5));
        Assert.NotNull(initialClaim);
        var dueAt = DateTime.UtcNow.AddMinutes(-1);
        await inbox.MarkRetryAsync(notification.ProviderCode, externalEventId, initialClaim.ProcessingId, "temporary failure", dueAt);

        var due = await inbox.ClaimDueAsync(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(-1), TimeSpan.FromMinutes(5), limit: 500);

        Assert.Contains(due, x => x.Notification.ExternalEventId == externalEventId);
    }

    [Fact]
    public async Task Pending_delivery_left_by_an_interruption_is_recovered()
    {
        await using var db = fixture.CreateBillingDb();
        var inbox = new ProviderWebhookInbox(db);
        var externalEventId = $"evt-{Guid.NewGuid():N}";
        var notification = Notification(externalEventId);
        await inbox.TryStoreAsync(notification, new string('D', 64));

        var recovered = await inbox.ClaimDueAsync(DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), TimeSpan.FromMinutes(5), limit: 500);

        Assert.Contains(recovered, x => x.Notification.ExternalEventId == externalEventId);
    }

    [Fact]
    public async Task Concurrent_duplicate_delivery_is_persisted_once()
    {
        var externalEventId = $"evt-{Guid.NewGuid():N}";
        var notification = Notification(externalEventId);

        async Task<bool> StoreAsync()
        {
            await using var db = fixture.CreateBillingDb();
            return await new ProviderWebhookInbox(db).TryStoreAsync(notification, new string('C', 64));
        }

        var results = await Task.WhenAll(StoreAsync(), StoreAsync());

        Assert.Single(results, x => x);
        await using var verification = fixture.CreateBillingDb();
        Assert.Equal(1, await verification.ProviderWebhookInbox.CountAsync(x => x.ProviderCode == "SAMPLE" && x.ExternalEventId == externalEventId));
    }

    [Fact]
    public async Task Concurrent_retry_workers_claim_a_delivery_only_once()
    {
        var externalEventId = $"evt-{Guid.NewGuid():N}";
        await using (var setupDb = fixture.CreateBillingDb())
            await new ProviderWebhookInbox(setupDb).TryStoreAsync(Notification(externalEventId), new string('E', 64));

        async Task<IReadOnlyCollection<ClaimedProviderEvent>> ClaimAsync()
        {
            await using var db = fixture.CreateBillingDb();
            return await new ProviderWebhookInbox(db).ClaimDueAsync(
                DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), TimeSpan.FromMinutes(5));
        }

        var claims = (await Task.WhenAll(ClaimAsync(), ClaimAsync()))
            .SelectMany(x => x)
            .Where(x => x.Notification.ExternalEventId == externalEventId)
            .ToArray();

        Assert.Single(claims);
    }

    private static NormalizedProviderEvent Notification(string externalEventId) =>
        new("sample", externalEventId, "subscription.updated", $"sub-{Guid.NewGuid():N}", DateTime.UtcNow,
            new Dictionary<string, string> { ["status"] = "active" });
}
