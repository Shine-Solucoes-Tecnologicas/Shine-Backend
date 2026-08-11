using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Scheduling.Infrastructure;

public interface IOutboxMessageHandler
{
    Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken);
}

public sealed class LoggingOutboxMessageHandler(IMessageBus messageBus) : IOutboxMessageHandler
{
    public Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        return messageBus.PublishAsync(message.EventType, message.Payload, cancellationToken);
    }
}

public sealed class SchedulingOutboxWorker(IServiceScopeFactory scopeFactory, IOptions<RabbitMqOptions> options, ILogger<SchedulingOutboxWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            if (!options.Value.Enabled) { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); continue; }
            try { await ProcessBatchAsync(stoppingToken); }
            catch (Exception exception) { logger.LogError(exception, "Scheduling outbox batch failed."); }
            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SchedulingDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<IOutboxMessageHandler>();
        var now = DateTime.UtcNow;
        var messages = await db.OutboxMessages.Where(x => x.ProcessedAtUtc == null && (x.LockedUntilUtc == null || x.LockedUntilUtc < now)).OrderBy(x => x.OccurredAtUtc).Take(50).ToArrayAsync(cancellationToken);
        foreach (var message in messages)
        {
            message.MarkAttempt(now.AddMinutes(1));
            await db.SaveChangesAsync(cancellationToken);
            try { await handler.HandleAsync(message, cancellationToken); message.MarkProcessed(DateTime.UtcNow); }
            catch (Exception exception) { logger.LogError(exception, "Failed to process outbox message {MessageId}.", message.Id); }
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
