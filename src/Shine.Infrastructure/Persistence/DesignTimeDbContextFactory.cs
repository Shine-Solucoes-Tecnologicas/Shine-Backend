using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Shine.Infrastructure.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShineDbContext>
{
    public ShineDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ShineDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=shine;Username=shine;Password=Shine")
            .Options;

        return new ShineDbContext(options);
    }
}
