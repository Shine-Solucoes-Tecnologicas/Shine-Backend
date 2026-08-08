using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public ShineDbContext Db { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShineDb")
            ?? "Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine";
        var options = new DbContextOptionsBuilder<ShineDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        Db = new ShineDbContext(options);
        await Db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
}

[CollectionDefinition("database")]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>;
