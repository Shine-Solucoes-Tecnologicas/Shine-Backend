using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using BusinessCatalog.Application;

namespace BusinessCatalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddBusinessCatalog(this IServiceCollection services, string connectionString) =>
        services.AddDbContext<BusinessCatalogDbContext>(options => options.UseNpgsql(connectionString))
            .AddScoped<IBusinessCatalogReader, BusinessCatalogReader>();
}
