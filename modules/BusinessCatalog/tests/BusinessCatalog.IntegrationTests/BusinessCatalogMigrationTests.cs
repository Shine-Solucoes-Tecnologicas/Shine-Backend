using BusinessCatalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Scheduling.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class BusinessCatalogMigrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Upgrade_from_the_previous_model_preserves_catalog_and_moves_scheduling_policies()
    {
        var schema = $"upgrade_catalog_{Guid.NewGuid():N}";
        var builder = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { SearchPath = schema };
        var connectionString = builder.ConnectionString;
        await ExecuteOnFixtureDatabaseAsync($"CREATE SCHEMA \"{schema}\"");
        try
        {
            var coreOptions = new DbContextOptionsBuilder<ShineDbContext>().UseNpgsql(connectionString).Options;
            await using (var core = new ShineDbContext(coreOptions)) await core.Database.MigrateAsync();

            var schedulingOptions = new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(connectionString).Options;
            await using (var scheduling = new SchedulingDbContext(schedulingOptions))
            {
                await scheduling.GetService<IMigrator>().MigrateAsync("20260824191640_TransferBusinessCatalogOwnership");
                var tenantId = Guid.NewGuid();
                var professionalId = Guid.NewGuid();
                var serviceId = Guid.NewGuid();
                var associationId = Guid.NewGuid();
                var createdAt = DateTime.UtcNow;
                await scheduling.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO "Professionals" ("Id", "TenantId", "UserId", "Name", "IsActive", "MaxConcurrentAppointments", "CreatedAtUtc")
                    VALUES ({professionalId}, {tenantId}, NULL, {'P' + professionalId.ToString("N")}, TRUE, {3}, {createdAt});
                    INSERT INTO "Services" ("Id", "TenantId", "Name", "DurationMinutes", "DurationAttributeKey", "MinutesPerAttributeUnit", "MinimumDurationMinutes", "MaximumDurationMinutes", "DurationRuleVersion", "IsActive", "CreatedAtUtc")
                    VALUES ({serviceId}, {tenantId}, {'S' + serviceId.ToString("N")}, {30}, {"length"}, {10}, {30}, {120}, {"v1"}, TRUE, {createdAt});
                    INSERT INTO "ProfessionalServices" ("Id", "TenantId", "ProfessionalId", "ServiceId", "DurationOverrideMinutes", "IsActive")
                    VALUES ({associationId}, {tenantId}, {professionalId}, {serviceId}, {55}, TRUE);
                    """);

                await scheduling.Database.MigrateAsync();
            }

            var catalogOptions = new DbContextOptionsBuilder<BusinessCatalogDbContext>().UseNpgsql(connectionString).Options;
            await using (var catalog = new BusinessCatalogDbContext(catalogOptions)) await catalog.Database.MigrateAsync();

            await using var verification = new NpgsqlConnection(connectionString);
            await verification.OpenAsync();
            Assert.Equal(3, await ScalarAsync<int>(verification, "SELECT \"MaxConcurrentAppointments\" FROM \"ProfessionalSchedulingSettings\""));
            Assert.Equal("length", await ScalarAsync<string>(verification, "SELECT \"DurationAttributeKey\" FROM \"ServiceSchedulingSettings\""));
            Assert.Equal(55, await ScalarAsync<int>(verification, "SELECT \"DurationOverrideMinutes\" FROM \"ProfessionalServiceSchedulingSettings\""));
            Assert.Equal(1L, await ScalarAsync<long>(verification, "SELECT COUNT(*) FROM \"Professionals\""));
            Assert.Equal(1L, await ScalarAsync<long>(verification, "SELECT COUNT(*) FROM \"Services\""));
            Assert.Equal(1L, await ScalarAsync<long>(verification, "SELECT COUNT(*) FROM \"ProfessionalServices\""));
            Assert.Equal(0L, await ScalarAsync<long>(verification, """
                SELECT COUNT(*) FROM information_schema.columns
                WHERE table_schema = current_schema()
                  AND ((table_name = 'Professionals' AND column_name = 'MaxConcurrentAppointments')
                    OR (table_name = 'ProfessionalServices' AND column_name = 'DurationOverrideMinutes')
                    OR (table_name = 'Services' AND column_name IN ('DurationAttributeKey', 'MinutesPerAttributeUnit', 'MinimumDurationMinutes', 'MaximumDurationMinutes', 'DurationRuleVersion')))
                """));
        }
        finally
        {
            await ExecuteOnFixtureDatabaseAsync($"DROP SCHEMA IF EXISTS \"{schema}\" CASCADE");
        }
    }

    private async Task ExecuteOnFixtureDatabaseAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
