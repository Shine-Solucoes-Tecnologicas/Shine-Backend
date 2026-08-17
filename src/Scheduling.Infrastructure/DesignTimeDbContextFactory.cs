using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Scheduling.Infrastructure;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SchedulingDbContext>
{
    public SchedulingDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("SHINE_SCHEDULING_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__ShineDb")
            ?? "Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine";
        return new SchedulingDbContext(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(connection).Options);
    }
}
