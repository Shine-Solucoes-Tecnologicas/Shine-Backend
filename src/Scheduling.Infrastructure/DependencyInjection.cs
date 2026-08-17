using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, string connectionString)
        => services.AddDbContext<SchedulingDbContext>(options => options.UseNpgsql(connectionString))
            .AddSingleton<Scheduling.Application.AvailabilitySlotCalculator>()
            .AddSingleton<Scheduling.Domain.IServiceDurationRule, Scheduling.Application.ConfiguredAttributeDurationRule>()
            .AddSingleton<Scheduling.Application.ServiceDurationEstimator>()
            .AddScoped<Scheduling.Application.IAppointmentEventPublisher, AppointmentEventPublisher>()
            .AddScoped<OperationalEventPublisher>()
            .AddScoped<IOutboxMessageHandler, LoggingOutboxMessageHandler>()
            .AddHostedService<SchedulingOutboxWorker>();
}
