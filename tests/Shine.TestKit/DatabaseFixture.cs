using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;
using Billing.Infrastructure;
using Scheduling.Infrastructure;
using BusinessCatalog.Infrastructure;
using Xunit;
using Npgsql;

namespace Shine.IntegrationTests;

public sealed class DatabaseFixture : IAsyncLifetime
{
    public ShineDbContext Db { get; private set; } = null!;
    private string connectionString = null!;
    private string baseConnectionString = null!;
    private string schemaName = null!;
    public string ConnectionString => connectionString;

    public async Task InitializeAsync()
    {
        baseConnectionString = Environment.GetEnvironmentVariable("ConnectionStrings__ShineDb")
            ?? "Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine";
        schemaName = CreateSchemaName();
        await using (var connection = new NpgsqlConnection(baseConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA \"{schemaName}\"";
            await command.ExecuteNonQueryAsync();
        }
        connectionString = new NpgsqlConnectionStringBuilder(baseConnectionString) { SearchPath = schemaName }.ConnectionString;
        var options = new DbContextOptionsBuilder<ShineDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        Db = new ShineDbContext(options, currentTenant: new UnscopedTenant(), tenantExecutionContext: new TestBypassContext());
        await Db.Database.MigrateAsync();
        await using var billingDb = CreateBillingDb();
        await billingDb.Database.MigrateAsync();
        await using var schedulingDb = CreateSchedulingDb();
        await schedulingDb.Database.MigrateAsync();
        await using var businessCatalogDb = CreateBusinessCatalogDb();
        await businessCatalogDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await Db.DisposeAsync();
        await using var connection = new NpgsqlConnection(baseConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS \"{schemaName}\" CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    private static string CreateSchemaName()
    {
        var assembly = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetName().Name)
            .FirstOrDefault(x => x?.EndsWith(".IntegrationTests", StringComparison.Ordinal) == true)
            ?? "integration";
        var normalized = new string(assembly.ToLowerInvariant().Where(char.IsLetterOrDigit).Take(20).ToArray());
        var candidate = $"test_{normalized}_{Environment.ProcessId}_{Guid.NewGuid():N}";
        return candidate[..Math.Min(63, candidate.Length)];
    }
    public ShineDbContext CreateDb(ICurrentTenant? currentTenant = null, ITenantExecutionContext? tenantExecutionContext = null)
    {
        var effectiveExecutionContext = tenantExecutionContext ?? (currentTenant is null ? new TestBypassContext() : null);
        return new(new DbContextOptionsBuilder<ShineDbContext>().UseNpgsql(connectionString).Options,
            currentTenant: currentTenant ?? new UnscopedTenant(),
            tenantExecutionContext: effectiveExecutionContext);
    }
    public ShineDbContext CreateUnscopedDb() =>
        new(new DbContextOptionsBuilder<ShineDbContext>().UseNpgsql(connectionString).Options,
            currentTenant: new UnscopedTenant());
    public ShineDbContext CreateDbWithInterceptors(ICurrentTenant currentTenant, ITenantExecutionContext? tenantExecutionContext,
        params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<ShineDbContext>().UseNpgsql(connectionString).AddInterceptors(interceptors).Options,
            currentTenant: currentTenant, tenantExecutionContext: tenantExecutionContext);
    public BillingDbContext CreateBillingDb() =>
        new(new DbContextOptionsBuilder<BillingDbContext>().UseNpgsql(connectionString).Options);
    public SchedulingDbContext CreateSchedulingDb(ICurrentTenant? currentTenant = null, ITenantExecutionContext? tenantExecutionContext = null)
    {
        var effectiveExecutionContext = tenantExecutionContext ?? (currentTenant is null ? new TestBypassContext() : null);
        return new(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(connectionString).Options,
            currentTenant ?? new UnscopedTenant(), effectiveExecutionContext);
    }
    public SchedulingDbContext CreateUnscopedSchedulingDb() =>
        new(new DbContextOptionsBuilder<SchedulingDbContext>().UseNpgsql(connectionString).Options,
            new UnscopedTenant());
    public BusinessCatalogDbContext CreateBusinessCatalogDb(ICurrentTenant? currentTenant = null, ITenantExecutionContext? tenantExecutionContext = null)
    {
        var effectiveExecutionContext = tenantExecutionContext ?? (currentTenant is null ? new TestBypassContext() : null);
        return new(new DbContextOptionsBuilder<BusinessCatalogDbContext>().UseNpgsql(connectionString).Options,
            currentTenant ?? new UnscopedTenant(), effectiveExecutionContext);
    }
    public BusinessCatalogDbContext CreateUnscopedBusinessCatalogDb() =>
        new(new DbContextOptionsBuilder<BusinessCatalogDbContext>().UseNpgsql(connectionString).Options,
            new UnscopedTenant());
    public BusinessCatalogReader CreateBusinessCatalogReader(ICurrentTenant? currentTenant = null, ITenantExecutionContext? tenantExecutionContext = null) =>
        new(CreateBusinessCatalogDb(currentTenant, tenantExecutionContext));
}

file sealed class UnscopedTenant : ICurrentTenant
{
    public Guid? TenantId => null;
    public Guid? UserTenantId => null;
    public IReadOnlyCollection<string> Roles => [];
    public bool HasCompleteContext => false;
}

file sealed class TestBypassContext : ITenantExecutionContext
{
    public Guid TenantId => throw new InvalidOperationException("Bypass has no active tenant.");
    public Guid? EffectiveTenantId => null;
    public bool IsBypass => true;
    public IDisposable EnterTenant(Guid tenantId) => NoopDisposable.Instance;
    public IDisposable EnterBypass() => NoopDisposable.Instance;
    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}
