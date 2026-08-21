using Billing.Domain;
using Billing.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shine.Infrastructure;

namespace Billing.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBillingInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<BillingDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IProviderWebhookInbox, ProviderWebhookInbox>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<ISubscriptionUnitOwnership, SubscriptionUnitOwnership>();
        services.AddScoped<ISubscriptionPlanCatalog, SubscriptionPlanCatalog>();
        services.AddScoped<SubscriptionActivationService>();
        services.AddScoped<SubscriptionPlanChangeService>();
        services.AddSingleton<IBillingClock, SystemBillingClock>();
        services.AddScoped<BillingWebhookProcessor>();
        services.AddScoped<BillingOutboxProcessor>();
        services.AddScoped<IDomainEventHandler<SubscriptionActivated>, SubscriptionActivatedHandler>();
        services.AddScoped<IDomainEventHandler<SubscriptionPlanChanged>, SubscriptionPlanChangedHandler>();
        services.AddScoped<IDomainEventHandler<SubscriptionCanceled>, SubscriptionCanceledHandler>();
        services.AddHostedService<BillingMaintenanceWorker>();
        return services;
    }
}
