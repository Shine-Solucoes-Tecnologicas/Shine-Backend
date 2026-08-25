using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shine.Domain;

namespace Scheduling.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddScheduling(this IServiceCollection services, string connectionString)
        => services.AddDbContext<SchedulingDbContext>(options => options.UseNpgsql(connectionString))
            .AddSingleton<Scheduling.Application.AvailabilitySlotCalculator>()
            .AddSingleton<Scheduling.Domain.IServiceDurationRule, Scheduling.Application.ConfiguredAttributeDurationRule>()
            .AddSingleton<Scheduling.Application.ServiceDurationEstimator>()
            .AddScoped<IDashboardWidgetProvider>(provider => new SchedulingDashboardWidgetProvider(
                provider.GetRequiredService<SchedulingDbContext>(), provider.GetRequiredService<Shine.Application.IBusinessCatalogReader>(), provider.GetRequiredService<Scheduling.Application.AvailabilitySlotCalculator>(), SchedulingWidgetKind.NextAppointments))
            .AddScoped<IDashboardWidgetProvider>(provider => new SchedulingDashboardWidgetProvider(
                provider.GetRequiredService<SchedulingDbContext>(), provider.GetRequiredService<Shine.Application.IBusinessCatalogReader>(), provider.GetRequiredService<Scheduling.Application.AvailabilitySlotCalculator>(), SchedulingWidgetKind.AverageOccupancy))
            .AddScoped<IDashboardWidgetProvider>(provider => new SchedulingDashboardWidgetProvider(
                provider.GetRequiredService<SchedulingDbContext>(), provider.GetRequiredService<Shine.Application.IBusinessCatalogReader>(), provider.GetRequiredService<Scheduling.Application.AvailabilitySlotCalculator>(), SchedulingWidgetKind.BusiestHours))
            .AddScoped<IDashboardWidgetProvider>(provider => new SchedulingDashboardWidgetProvider(
                provider.GetRequiredService<SchedulingDbContext>(), provider.GetRequiredService<Shine.Application.IBusinessCatalogReader>(), provider.GetRequiredService<Scheduling.Application.AvailabilitySlotCalculator>(), SchedulingWidgetKind.QuietestHours))
            .AddScoped<Scheduling.Application.IAppointmentEventPublisher, AppointmentEventPublisher>()
            .AddScoped<Shine.Application.ICustomerHistoryReader, CustomerAppointmentHistoryReader>()
            .AddScoped<OperationalEventPublisher>()
            .AddScoped<IOutboxMessageHandler, LoggingOutboxMessageHandler>()
            .AddHostedService<SchedulingOutboxWorker>();
}
