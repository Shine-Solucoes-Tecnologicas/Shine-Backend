using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Shine.Domain;
using Shine.Application;

namespace Shine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddOptions<RabbitMqOptions>();
        services.AddSingleton<IMessageBus, RabbitMqMessageBus>();
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddSingleton<IPasswordHashService, Pbkdf2PasswordHashService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IFunctionalSettings, FunctionalSettings>();
        services.AddScoped<IFeatureFlags, FeatureFlags>();
        services.AddScoped<IModuleAccess, ModuleAccessService>();
        services.AddScoped<IPlanAccess, PlanAccess>();
        services.AddScoped<IEntitlementAccess, EntitlementAccess>();
        services.AddScoped<IEntitlementLimitGuard, EntitlementLimitGuard>();
        services.AddSingleton<IEntitlementKeyCatalog, EntitlementKeyCatalog>();
        services.AddOptions<PasswordPolicyOptions>()
            .BindConfiguration("PasswordPolicy")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddOptions<JwtOptions>()
            .BindConfiguration("Jwt")
            .Validate(options => JwtSigningKeyRing.TryValidate(options, out _), "JWT configuration is invalid.")
            .ValidateOnStart();
        services.AddOptions<LoginSecurityOptions>().BindConfiguration("LoginSecurity");
        services.AddSingleton<JwtSigningKeyRing>();
        services.AddSingleton<IAccessTokenService, JwtAccessTokenService>();
        services.AddSingleton<IModuleCatalog>(_ => CreateModuleCatalog());
        services.AddSingleton<IDashboardWidgetCatalog>(_ => CreateDashboardWidgetCatalog());
        services.AddScoped<IDashboardWidgetResolver, DashboardWidgetResolver>();
        services.AddScoped<DashboardLayoutService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ITenantExecutionContext, TenantExecutionContext>();
        services.AddScoped<IOperationalLogWriter, OperationalLogWriter>();
        services.AddScoped<IPermissionAuthorization, PermissionAuthorization>();
        services.AddScoped<ICustomerAccountAuthorization, CustomerAccountAuthorization>();
        services.AddScoped<ICustomerManagement, CustomerManagement>();
        services.AddScoped<ICustomerReferenceValidator, CustomerManagement>();
        services.AddOptions<FileStorageOptions>().BindConfiguration("FileStorage");
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<StoredFileDeletionProcessor>();
        services.AddOptions<ConnectionStringOptions>()
            .BindConfiguration("ConnectionStrings")
            .ValidateDataAnnotations()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ShineDb), "ConnectionStrings:ShineDb must be configured.")
            .ValidateOnStart();
        services.AddDbContext<ShineDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }

    private static IModuleCatalog CreateModuleCatalog()
    {
        var catalog = new ModuleCatalog();
        catalog.Register(ModuleDescriptor.Create(
            "CORE",
            "Core platform",
            "Shared platform capabilities and infrastructure contracts.",
            dependencies: []) with
        {
            Endpoints = [new ModuleEndpoint("GET", "/api/modules")],
            Services = [new ModuleService("IModuleCatalog", "Singleton")]
        });
        catalog.Register(ModuleDescriptor.Create(
            "SCHEDULING",
            "Scheduling",
            "Professionals, services, availability and appointments.",
            dependencies: ["CORE"]) with
        {
            Endpoints = [new ModuleEndpoint("GET", "/api/scheduling")],
            Services = [new ModuleService("SchedulingDbContext", "Scoped")]
        });
        return catalog;
    }

    private static IDashboardWidgetCatalog CreateDashboardWidgetCatalog()
    {
        var catalog = new DashboardWidgetCatalog();
        catalog.Register(new EmptyDashboardWidgetProvider(new DashboardWidgetDescriptor(
            "core.welcome", "CORE", "Visão geral", "Resumo da organização", "dashboard.read", dataSource: "core")));
        catalog.Register(new EmptyDashboardWidgetProvider(new DashboardWidgetDescriptor(
            "scheduling.next-appointments", "SCHEDULING", "Próximos agendamentos", "Próximos compromissos da agenda", "scheduling.read", defaultWidth: 2, defaultHeight: 2, dataSource: "scheduling.appointments")));
        return catalog;
    }

    private sealed class EmptyDashboardWidgetProvider(DashboardWidgetDescriptor descriptor) : IDashboardWidgetProvider
    {
        public DashboardWidgetDescriptor Descriptor { get; } = descriptor;
        public Task<DashboardWidgetData> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DashboardWidgetData(Descriptor.WidgetKey, new Dictionary<string, object?> { ["items"] = Array.Empty<object>() }));
    }
}
