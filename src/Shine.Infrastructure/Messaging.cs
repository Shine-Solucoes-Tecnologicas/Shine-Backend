using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Shine.Infrastructure;

public sealed class RabbitMqOptions
{
    public bool Enabled { get; init; }
    public string HostName { get; init; } = "localhost";
    public int Port { get; init; } = 5672;
    public string UserName { get; init; } = "guest";
    public string Password { get; init; } = "guest";
    public string VirtualHost { get; init; } = "/";
    public string ExchangeName { get; init; } = "shine.events";
}

public interface IMessageBus
{
    Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default);
}

public sealed class RabbitMqMessageBus(IOptions<RabbitMqOptions> options, ILogger<RabbitMqMessageBus> logger) : IMessageBus, IAsyncDisposable
{
    private readonly RabbitMqOptions settings = options.Value;
    private readonly SemaphoreSlim publishLock = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;

    public async Task PublishAsync(string eventType, string payload, CancellationToken cancellationToken = default)
    {
        if (!settings.Enabled) throw new InvalidOperationException("RabbitMQ messaging is disabled.");
        await publishLock.WaitAsync(cancellationToken);
        try
        {
            if (channel is null || !channel.IsOpen) await RecreateChannelAsync(cancellationToken);
            var activeChannel = channel!;
            await activeChannel.ExchangeDeclareAsync(settings.ExchangeName, ExchangeType.Topic, durable: true, cancellationToken: cancellationToken);
            var body = Encoding.UTF8.GetBytes(payload);
            var properties = new BasicProperties { Persistent = true, ContentType = "application/json", Type = eventType };
            await activeChannel.BasicPublishAsync(settings.ExchangeName, eventType, mandatory: false, properties, body, cancellationToken);
            logger.LogDebug("Published {EventType} to RabbitMQ exchange {ExchangeName}.", eventType, settings.ExchangeName);
        }
        catch
        {
            await DisposeChannelAsync();
            throw;
        }
        finally { publishLock.Release(); }
    }

    private async Task RecreateChannelAsync(CancellationToken cancellationToken)
    {
        await DisposeChannelAsync();
        var factory = new ConnectionFactory { HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName, Password = settings.Password, VirtualHost = settings.VirtualHost };
        connection = await factory.CreateConnectionAsync(cancellationToken);
        channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    }

    private async Task DisposeChannelAsync()
    {
        if (channel is not null) await channel.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        channel = null;
        connection = null;
    }

    public async ValueTask DisposeAsync()
    {
        await publishLock.WaitAsync();
        try { await DisposeChannelAsync(); }
        finally { publishLock.Release(); publishLock.Dispose(); }
    }
}
