using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace BusinessCatalog.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BusinessCatalogDbContext>
{
    public BusinessCatalogDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__ShineDb")
            ?? "Host=localhost;Port=5432;Database=shine;Username=shine;Password=shine";
        return new BusinessCatalogDbContext(new DbContextOptionsBuilder<BusinessCatalogDbContext>().UseNpgsql(connection).Options);
    }
}
