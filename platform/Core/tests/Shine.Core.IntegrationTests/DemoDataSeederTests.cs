using BusinessCatalog.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Scheduling.Infrastructure;
using Shine.Api;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.IntegrationTests;

namespace Shine.Core.IntegrationTests;

public sealed class DemoDataSeederTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public void Target_guard_refuses_non_demo_database()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoData:Enabled"] = "true",
            ["ConnectionStrings:ShineDb"] = "Host=localhost;Database=shine;Username=shine;Password=shine"
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => new DemoDataTargetGuard(configuration, new DevelopmentEnvironment()).EnsureAllowed());
        Assert.Contains(DemoDataSeeder.DatabaseName, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Target_guard_refuses_non_development_environment()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoData:Enabled"] = "true",
            ["ConnectionStrings:ShineDb"] = $"Host=localhost;Database={DemoDataSeeder.DatabaseName};Username=shine;Password=shine"
        }).Build();

        Assert.Throws<InvalidOperationException>(() => new DemoDataTargetGuard(configuration, new DevelopmentEnvironment { EnvironmentName = Environments.Production }).EnsureAllowed());
    }

    [Fact]
    public async Task Seed_is_idempotent_and_preserves_unit_isolation()
    {
        var currentTenant = new AnonymousTenant();
        var execution = new TenantExecutionContext(currentTenant);
        await using var core = fixture.CreateDb(currentTenant, execution);
        await using var catalog = fixture.CreateBusinessCatalogDb(currentTenant, execution);
        await using var scheduling = fixture.CreateSchedulingDb(currentTenant, execution);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DemoData:Password"] = "integration-only-password"
        }).Build();
        var seeder = new DemoDataSeeder(core, catalog, scheduling, execution, new Pbkdf2PasswordHashService(), configuration, new AllowTarget());

        await seeder.SeedAsync();
        await seeder.SeedAsync();

        Assert.Equal(3, await core.Users.CountAsync(x => x.Email.EndsWith(".demo@example.test")));
        var account = await core.CustomerAccounts.SingleAsync(x => x.Name == DemoDataSeeder.AccountName);
        var units = await core.Tenants.Where(x => x.CustomerAccountId == account.Id).OrderBy(x => x.Name).ToArrayAsync();
        Assert.Equal(2, units.Length);

        foreach (var unit in units)
        {
            using var tenantScope = execution.EnterTenant(unit.Id);
            Assert.Single(await core.Customers.ToArrayAsync());
            Assert.Single(await catalog.Professionals.ToArrayAsync());
            Assert.Single(await catalog.Services.ToArrayAsync());
            Assert.Equal(5, await scheduling.AvailabilityRules.CountAsync());
            Assert.Equal(3, await scheduling.Appointments.CountAsync());
        }

        var viewer = await core.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(DemoDataSeeder.ViewerEmail));
        var viewerUnits = await core.CustomerAccountUserRoleUnits.Where(x => x.UserId == viewer.Id).Select(x => x.UnitId).ToArrayAsync();
        Assert.Single(viewerUnits);
        Assert.Contains(units, x => x.Id == viewerUnits[0] && x.Name == "Aurora Centro");
    }

    private sealed class AllowTarget : IDemoDataTargetGuard
    {
        public void EnsureAllowed() { }
    }

    private sealed class AnonymousTenant : ICurrentTenant
    {
        public Guid? TenantId => null;
        public Guid? UserTenantId => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => false;
    }

    private sealed class DevelopmentEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Shine.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
