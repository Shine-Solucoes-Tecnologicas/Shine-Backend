using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scheduling.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("SHINE_SCHEDULING_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=shine;Username=shine;Password=Shine";
        return new SchedulingDbContext(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(connection).Options);
    }
}
