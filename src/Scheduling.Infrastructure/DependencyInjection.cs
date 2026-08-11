using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Scheduling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, string connectionString)
        => services.AddDbContext<SchedulingDbContext>(options => options.UseNpgsql(connectionString))
            .AddSingleton<Scheduling.Application.AvailabilitySlotCalculator>()
            .AddScoped<Scheduling.Application.IAppointmentEventPublisher, AppointmentEventPublisher>()
            .AddScoped<IOutboxMessageHandler, LoggingOutboxMessageHandler>()
            .AddHostedService<SchedulingOutboxWorker>();
}
