using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public ShineDbContext Db { get; private set; } = null!;
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShineDb")
            ?? "Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine";
        var options = new DbContextOptionsBuilder<ShineDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        Db = new ShineDbContext(options, currentTenant: new UnscopedTenant());
        await Db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
    public ShineDbContext CreateDb() => new(new DbContextOptionsBuilder<ShineDbContext>().UseNpgsql(connectionString).Options, currentTenant: new UnscopedTenant());
}

file sealed class UnscopedTenant : ICurrentTenant
{
    public Guid? TenantId => null;
    public Guid? UserTenantId => null;
    public IReadOnlyCollection<string> Roles => [];
    public bool HasCompleteContext => false;
}

[CollectionDefinition("database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
