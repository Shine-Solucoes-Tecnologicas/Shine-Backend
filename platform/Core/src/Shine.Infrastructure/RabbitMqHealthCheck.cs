using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace Shine.Infrastructure;

public sealed class RabbitMqHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var settings = options.Value;
        if (!settings.Enabled) return HealthCheckResult.Healthy("RabbitMQ is disabled.");
        try
        {
            var factory = new ConnectionFactory { HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName, Password = settings.Password, VirtualHost = settings.VirtualHost };
            await using var connection = await factory.CreateConnectionAsync(cancellationToken);
            return HealthCheckResult.Healthy("RabbitMQ connection is available.");
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy("RabbitMQ connection is unavailable.", exception);
        }
    }
}
