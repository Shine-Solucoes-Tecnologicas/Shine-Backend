using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;
using Microsoft.Extensions.Options;

namespace Shine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddMemoryCache();
        services.AddSingleton<ICacheService, MemoryCacheService>();
        services.AddSingleton<IPasswordHashService, Pbkdf2PasswordHashService>();
        services.AddOptions<PasswordPolicyOptions>()
            .BindConfiguration("PasswordPolicy")
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IPasswordPolicy, PasswordPolicy>();
        services.AddOptions<JwtOptions>().BindConfiguration("Jwt");
        services.AddSingleton<IAccessTokenService, HmacAccessTokenService>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, CurrentUser>();
        services.AddScoped<ICurrentTenant, CurrentTenant>();
        services.AddScoped<ITenantExecutionContext, TenantExecutionContext>();
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
}
