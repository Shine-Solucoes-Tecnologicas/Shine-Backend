using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;
using Billing.Infrastructure;
using Scheduling.Infrastructure;
using Xunit;

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
        Db = new ShineDbContext(options, currentTenant: new UnscopedTenant(), tenantExecutionContext: new TestBypassContext());
        await Db.Database.MigrateAsync();
        await using var billingDb = CreateBillingDb();
        await billingDb.Database.MigrateAsync();
        await using var schedulingDb = CreateSchedulingDb();
        await schedulingDb.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await Db.DisposeAsync();
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
