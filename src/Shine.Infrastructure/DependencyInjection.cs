using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;
using Microsoft.Extensions.Options;
using Shine.Domain;

namespace Shine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddSingleton<IPasswordHashService, Pbkdf2PasswordHashService>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<IFunctionalSettings, FunctionalSettings>();
        services.AddScoped<IFeatureFlags, FeatureFlags>();
        services.AddScoped<IModuleAccess, ModuleAccessService>();
        services.AddScoped<IPlanAccess, PlanAccess>();
        services.AddOptions<PasswordPolicyOptions>()
            .BindConfiguration("PasswordPolicy")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddOptions<JwtOptions>().BindConfiguration("Jwt");
        services.AddSingleton<IAccessTokenService, HmacAccessTokenService>();
        services.AddSingleton<IModuleCatalog>(_ => CreateModuleCatalog());
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ITenantExecutionContext, TenantExecutionContext>();
        services.AddScoped<IPermissionAuthorization, PermissionAuthorization>();
        services.AddOptions<FileStorageOptions>().BindConfiguration("FileStorage");
        services.AddSingleton<IFileStorage, LocalFileStorage>();
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
        return catalog;
    }
}
