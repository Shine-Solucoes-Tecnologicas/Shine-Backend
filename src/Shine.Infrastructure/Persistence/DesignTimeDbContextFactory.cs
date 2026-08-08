using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shine.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShineDbContext>
{
    public ShineDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShineDb")
            ?? "Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine";

        var options = new DbContextOptionsBuilder<ShineDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        return new ShineDbContext(options);
    }
}
