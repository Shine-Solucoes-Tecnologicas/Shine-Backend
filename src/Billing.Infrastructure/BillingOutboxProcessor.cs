using System.Text.Json;
using Billing.Application;
using Billing.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.Infrastructure;

public sealed class BillingOutboxProcessor(BillingDbContext db, IServiceProvider services, IBillingClock clock)
{
    public async Task<int> ProcessDueAsync(int limit = 50, CancellationToken cancellationToken = default)
    {
        var claims = await ClaimDueAsync(limit, cancellationToken);
        var processed = 0;
        foreach (var claim in claims)
        {
            try
            {
                await DispatchAsync(Deserialize(claim.EventType, claim.PayloadJson), cancellationToken);
                await CompleteAsync(claim, cancellationToken);
                processed++;
            }
            catch (Exception exception) when (IsTransient(exception))
            {
                await RetryAsync(claim, exception.Message, cancellationToken);
            }
            catch (Exception exception)
            {
                await FailAsync(claim, exception.Message, cancellationToken);
            }
        }
        return processed;
    }

    private async Task<IReadOnlyCollection<ClaimedBillingOutboxMessage>> ClaimDueAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(limit));
        var now = clock.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var pending = (int)BillingOutboxStatus.Pending;
        var retry = (int)BillingOutboxStatus.RetryScheduled;
        var processing = (int)BillingOutboxStatus.Processing;
        var messages = await db.OutboxMessages.FromSqlInterpolated($$"""
            SELECT * FROM "BillingOutbox"
            WHERE "Status" = {{pending}}
               OR ("Status" = {{retry}} AND "NextAttemptAtUtc" <= {{now}})
               OR ("Status" = {{processing}} AND "LockedUntilUtc" <= {{now}})
            ORDER BY "OccurredAtUtc"
            FOR UPDATE SKIP LOCKED
            LIMIT {{limit}}
            """).ToArrayAsync(cancellationToken);
        var claims = messages.Select(message => new ClaimedBillingOutboxMessage(
            message.EventId, message.Claim(now, TimeSpan.FromMinutes(5)), message.EventType, message.PayloadJson)).ToArray();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return claims;
    }

    private async Task DispatchAsync(ISubscriptionIntegrationEvent domainEvent, CancellationToken cancellationToken)
    {
        var handlerType = typeof(Shine.Infrastructure.IDomainEventHandler<>).MakeGenericType(domainEvent.GetType());
        foreach (var handler in services.GetServices(handlerType))
        {
            var method = handlerType.GetMethod(nameof(Shine.Infrastructure.IDomainEventHandler<Shine.Domain.IDomainEvent>.HandleAsync))!;
            await (Task)method.Invoke(handler, [domainEvent, cancellationToken])!;
        }
    }

    private Task CompleteAsync(ClaimedBillingOutboxMessage claim, CancellationToken cancellationToken) =>
        UpdateAsync(claim, item => item.MarkProcessed(claim.ProcessingId, clock.UtcNow), cancellationToken);
    private Task RetryAsync(ClaimedBillingOutboxMessage claim, string error, CancellationToken cancellationToken) =>
        UpdateAsync(claim, item => item.MarkRetry(claim.ProcessingId, error, clock.UtcNow.AddMinutes(1)), cancellationToken);
    private Task FailAsync(ClaimedBillingOutboxMessage claim, string error, CancellationToken cancellationToken) =>
        UpdateAsync(claim, item => item.MarkFailed(claim.ProcessingId, error), cancellationToken);

    private async Task UpdateAsync(ClaimedBillingOutboxMessage claim, Action<BillingOutboxMessage> update, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var item = await db.OutboxMessages.SingleAsync(x => x.EventId == claim.EventId, cancellationToken);
        if (!item.IsOwnedBy(claim.ProcessingId)) return;
        update(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsTransient(Exception exception) =>
        exception is TransientBillingProviderException or TimeoutException ||
        exception is Npgsql.NpgsqlException { IsTransient: true } ||
        exception.InnerException is not null && IsTransient(exception.InnerException);

    private static ISubscriptionIntegrationEvent Deserialize(string eventType, string payloadJson) => eventType switch
    {
        nameof(SubscriptionActivated) => Required<SubscriptionActivated>(payloadJson),
        nameof(SubscriptionPlanChangeRequested) => Required<SubscriptionPlanChangeRequested>(payloadJson),
        nameof(SubscriptionPlanChanged) => Required<SubscriptionPlanChanged>(payloadJson),
        nameof(SubscriptionCancellationRequested) => Required<SubscriptionCancellationRequested>(payloadJson),
        nameof(SubscriptionCanceled) => Required<SubscriptionCanceled>(payloadJson),
        _ => throw new InvalidOperationException($"Unsupported billing outbox event '{eventType}'.")
    };

    private static T Required<T>(string payloadJson) where T : ISubscriptionIntegrationEvent =>
        JsonSerializer.Deserialize<T>(payloadJson) ?? throw new InvalidOperationException("Billing outbox payload is invalid.");
}
