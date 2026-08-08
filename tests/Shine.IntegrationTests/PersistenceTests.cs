using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.IntegrationTests;

 [Collection("database")]
public sealed class PersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Database_is_reachable_and_migrations_are_applied()
    {
        await using var db = fixture.Db;
        Assert.True(await db.Database.CanConnectAsync());

        var pending = (await db.Database.GetPendingMigrationsAsync()).ToArray();
        Assert.Empty(pending);
        await db.Database.ExecuteSqlRawAsync("SELECT 1");
    }
}
