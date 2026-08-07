using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ShineDbContext>(options => options.UseNpgsql(connectionString));
        return services;
    }
}
