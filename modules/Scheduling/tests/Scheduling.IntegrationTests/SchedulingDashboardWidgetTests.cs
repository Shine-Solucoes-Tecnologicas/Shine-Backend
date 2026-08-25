using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Scheduling.Domain;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class SchedulingDashboardWidgetTests(DatabaseFixture fixture)
{
    private const string JwtSecret = "integration-tests-only-secret-with-at-least-32-bytes";

    [Fact]
    public async Task Widgets_execute_through_http_with_permission_period_and_tenant_isolation()
    {
        var user = new User($"dashboard-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Dashboard account {Guid.NewGuid():N}");
        var unit = new Tenant($"Dashboard unit {Guid.NewGuid():N}");
        var foreignUnit = new Tenant($"Foreign dashboard unit {Guid.NewGuid():N}");
        var plan = new Plan($"DASH-{Guid.NewGuid():N}", "Dashboard plan");
        plan.Modules.Add(new PlanModule(plan.Id, "SCHEDULING"));
        unit.AssignToCustomerAccount(account.Id);
        foreignUnit.AssignToCustomerAccount(account.Id);
        var membership = new UserTenant(user.Id, unit.Id, user.Id, false);

        await using (var db = fixture.CreateDb())
        {
            db.AddRange(user, account, unit, foreignUnit, plan, membership,
                new TenantPlan(unit.Id, plan.Id), new CustomerAccountUser(account.Id, user.Id));
            await db.SaveChangesAsync();
            await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, user.Id);
            var viewer = await db.CustomerAccountRoles.SingleAsync(x =>
                x.AccountId == account.Id && x.Name == CustomerAccountRole.ViewerName);
            db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(
                account.Id, user.Id, viewer.Id, allUnits: true, allModules: true));
            await db.SaveChangesAsync();
        }

        var date = new DateOnly(2026, 8, 24);
        var fromUtc = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
        var toUtc = fromUtc.AddHours(3);
        var professional = new Professional(unit.Id, "Professional");
        var service = new Service(unit.Id, "Service", 30);
        var foreignProfessional = new Professional(foreignUnit.Id, "Foreign professional");
        var foreignService = new Service(foreignUnit.Id, "Foreign service", 30);
        var settings = new SchedulingSettings(unit.Id);
        settings.Update(15, 0, 0, "America/Sao_Paulo", ConflictMode.Block, 1);

        await using (var catalog = fixture.CreateBusinessCatalogDb())
        {
            catalog.AddRange(professional, service, new ProfessionalService(unit.Id, professional.Id, service.Id),
                foreignProfessional, foreignService,
                new ProfessionalService(foreignUnit.Id, foreignProfessional.Id, foreignService.Id));
            await catalog.SaveChangesAsync();
        }

        await using (var db = fixture.CreateSchedulingDb())
        {
            db.AddRange(settings, new AvailabilityRule(unit.Id, professional.Id, date.DayOfWeek,
                    new TimeSpan(9, 0, 0), new TimeSpan(12, 0, 0)),
                new Appointment(unit.Id, professional.Id, service.Id, "Visible customer", "hidden@example.test",
                    fromUtc.UtcDateTime.AddHours(1), fromUtc.UtcDateTime.AddHours(1.5)),
                new Appointment(foreignUnit.Id, foreignProfessional.Id, foreignService.Id, "Foreign customer", "foreign@example.test",
                    fromUtc.UtcDateTime.AddMinutes(30), fromUtc.UtcDateTime.AddHours(1)));
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(user.Id, unit.Id, membership.UserTenantId);

        var manifest = await client.GetAsync("/api/dashboard/widgets");
        Assert.Equal(HttpStatusCode.OK, manifest.StatusCode);
        using (var json = JsonDocument.Parse(await manifest.Content.ReadAsStringAsync()))
        {
            var keys = json.RootElement.EnumerateArray()
                .Select(x => x.GetProperty("descriptor").GetProperty("widgetKey").GetString()).ToHashSet();
            Assert.Contains("SCHEDULING.NEXT-APPOINTMENTS", keys);
            Assert.Contains("SCHEDULING.AVERAGE-OCCUPANCY", keys);
            Assert.Contains("SCHEDULING.BUSIEST-HOURS", keys);
            Assert.Contains("SCHEDULING.QUIETEST-HOURS", keys);
        }

        var next = await GetWidgetAsync(client, "scheduling.next-appointments", fromUtc, toUtc);
        var nextItems = next.GetProperty("values").GetProperty("items");
        Assert.Equal(1, nextItems.GetArrayLength());
        Assert.Equal("Visible customer", nextItems[0].GetProperty("customerName").GetString());
        Assert.False(next.ToString().Contains("hidden@example.test", StringComparison.Ordinal));
        Assert.False(next.ToString().Contains("Foreign customer", StringComparison.Ordinal));

        var occupancy = await GetWidgetAsync(client, "scheduling.average-occupancy", fromUtc, toUtc);
        Assert.Equal(180, occupancy.GetProperty("values").GetProperty("capacityMinutes").GetInt64());
        Assert.Equal(30, occupancy.GetProperty("values").GetProperty("bookedMinutes").GetInt64());
        Assert.Equal(16.67m, occupancy.GetProperty("values").GetProperty("occupancyPercentage").GetDecimal());

        var busiest = await GetWidgetAsync(client, "scheduling.busiest-hours", fromUtc, toUtc);
        Assert.Equal(10, busiest.GetProperty("values").GetProperty("items")[0].GetProperty("hour").GetInt32());
        var quietest = await GetWidgetAsync(client, "scheduling.quietest-hours", fromUtc, toUtc);
        Assert.Equal(10, quietest.GetProperty("values").GetProperty("items")[0].GetProperty("hour").GetInt32());

        var excessivePeriod = await client.GetAsync(WidgetPath("scheduling.average-occupancy", fromUtc, fromUtc.AddDays(32)));
        Assert.Equal(HttpStatusCode.BadRequest, excessivePeriod.StatusCode);
    }

    [Fact]
    public async Task Widget_data_is_not_exposed_without_the_widget_permission()
    {
        var user = new User($"dashboard-denied-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Dashboard denied {Guid.NewGuid():N}");
        var unit = new Tenant($"Dashboard denied unit {Guid.NewGuid():N}");
        var plan = new Plan($"DASH-DENIED-{Guid.NewGuid():N}", "Dashboard denied plan");
        plan.Modules.Add(new PlanModule(plan.Id, "SCHEDULING"));
        unit.AssignToCustomerAccount(account.Id);
        var membership = new UserTenant(user.Id, unit.Id, user.Id, false);
        await using (var db = fixture.CreateDb())
        {
            db.AddRange(user, account, unit, plan, membership, new TenantPlan(unit.Id, plan.Id),
                new CustomerAccountUser(account.Id, user.Id));
            await db.SaveChangesAsync();
            await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, user.Id);
        }

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(user.Id, unit.Id, membership.UserTenantId);
        var fromUtc = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);

        var response = await client.GetAsync(WidgetPath("scheduling.next-appointments", fromUtc, fromUtc.AddDays(1)));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static async Task<JsonElement> GetWidgetAsync(HttpClient client, string key,
        DateTimeOffset fromUtc, DateTimeOffset toUtc)
    {
        var response = await client.GetAsync(WidgetPath(key, fromUtc, toUtc));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static string WidgetPath(string key, DateTimeOffset fromUtc, DateTimeOffset toUtc) =>
        $"/api/dashboard/widgets/{key}/data?fromUtc={Uri.EscapeDataString(fromUtc.ToString("O"))}&toUtc={Uri.EscapeDataString(toUtc.ToString("O"))}";

    private static AuthenticationHeaderValue Bearer(Guid userId, Guid tenantId, Guid userTenantId)
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = JwtSecret,
            Issuer = "Shine",
            Audience = "Shine.Api",
            AccessTokenMinutes = 5
        });
        var service = new JwtAccessTokenService(options, new JwtSigningKeyRing(options));
        return new AuthenticationHeaderValue("Bearer", service.Create(userId, tenantId, userTenantId, []).Token);
    }

    private string ConnectionString() => fixture.ConnectionString;

    private sealed class ApiFactory(string connectionString, string jwtSecret) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ShineDb", connectionString);
            builder.UseSetting("Jwt:Secret", jwtSecret);
            builder.UseSetting("Jwt:Issuer", "Shine");
            builder.UseSetting("Jwt:Audience", "Shine.Api");
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
