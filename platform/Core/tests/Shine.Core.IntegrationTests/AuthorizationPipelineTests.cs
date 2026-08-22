using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class AuthorizationPipelineTests(DatabaseFixture fixture)
{
    private const string JwtSecret = "integration-tests-only-secret-with-at-least-32-bytes";

    [Fact]
    public async Task Commercial_endpoint_executes_the_real_authentication_and_authorization_pipeline()
    {
        await using var db = fixture.CreateDb();
        await AuthorizationSeed.EnsureGlobalRolesAsync(db);

        var regularUser = new User($"regular-{Guid.NewGuid():N}@example.test", "hash");
        var commercialUser = new User($"commercial-http-{Guid.NewGuid():N}@example.test", "hash");
        var commercialRole = await db.GlobalRoles.SingleAsync(x => x.Name == GlobalRole.CommercialManagerName);
        db.Users.AddRange(regularUser, commercialUser);
        db.UserGlobalRoles.Add(new UserGlobalRole(commercialUser.Id, commercialRole.Id));
        await db.SaveChangesAsync();

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();

        var anonymous = await client.GetAsync("/api/admin/billing/contracts");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(regularUser.Id);
        var authenticatedWithoutPermission = await client.GetAsync("/api/admin/billing/contracts");
        Assert.Equal(HttpStatusCode.Forbidden, authenticatedWithoutPermission.StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(commercialUser.Id);
        var authorized = await client.GetAsync("/api/admin/billing/contracts");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task Administrative_dashboard_executes_the_real_global_authorization_pipeline()
    {
        await using var db = fixture.CreateDb();
        await AuthorizationSeed.EnsureGlobalRolesAsync(db);
        var regularUser = new User($"dashboard-regular-{Guid.NewGuid():N}@example.test", "hash");
        var platformAdministrator = new User($"dashboard-admin-{Guid.NewGuid():N}@example.test", "hash");
        var administratorRole = await db.GlobalRoles.SingleAsync(x => x.Name == GlobalRole.PlatformAdminName);
        db.Users.AddRange(regularUser, platformAdministrator);
        db.UserGlobalRoles.Add(new UserGlobalRole(platformAdministrator.Id, administratorRole.Id));
        await db.SaveChangesAsync();

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/dashboard/summary")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(regularUser.Id);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/dashboard/summary")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(platformAdministrator.Id);
        var authorized = await client.GetAsync("/api/admin/dashboard/summary");
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }

    [Fact]
    public async Task Reschedule_requires_scheduling_management_instead_of_generic_administration()
    {
        await using var db = fixture.CreateDb();
        var genericAdministrator = new User($"generic-admin-{Guid.NewGuid():N}@example.test", "hash");
        var schedulingEditor = new User($"scheduling-editor-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Scheduling auth {Guid.NewGuid():N}");
        var unit = new Tenant($"Scheduling unit {Guid.NewGuid():N}");
        var plan = new Shine.Domain.Plan($"SCHED-AUTH-{Guid.NewGuid():N}", "Scheduling auth plan");
        plan.Modules.Add(new Shine.Domain.PlanModule(plan.Id, "SCHEDULING"));
        unit.AssignToCustomerAccount(account.Id);
        var adminMembership = new UserTenant(genericAdministrator.Id, unit.Id, genericAdministrator.Id, false);
        var editorMembership = new UserTenant(schedulingEditor.Id, unit.Id, schedulingEditor.Id, false);
        db.AddRange(genericAdministrator, schedulingEditor, account, unit, plan,
            new Shine.Domain.TenantPlan(unit.Id, plan.Id),
            new CustomerAccountUser(account.Id, genericAdministrator.Id),
            new CustomerAccountUser(account.Id, schedulingEditor.Id), adminMembership, editorMembership);
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, genericAdministrator.Id);
        var editorRole = await db.CustomerAccountRoles.SingleAsync(x =>
            x.AccountId == account.Id && x.Name == CustomerAccountRole.EditorName);
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(
            account.Id, schedulingEditor.Id, editorRole.Id, allUnits: true, allModules: true));
        await db.SaveChangesAsync();

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        var startsAtUtc = DateTime.UtcNow.AddDays(2);
        var body = new
        {
            startsAtUtc,
            endsAtUtc = startsAtUtc.AddMinutes(30),
            expectedVersion = Guid.NewGuid(),
            allowConflict = false
        };
        var path = $"/api/scheduling/appointments/{Guid.NewGuid()}/reschedule";

        client.DefaultRequestHeaders.Authorization = Bearer(genericAdministrator.Id, unit.Id, adminMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync(path, body)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(schedulingEditor.Id, unit.Id, editorMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync(path, body)).StatusCode);
    }

    private static AuthenticationHeaderValue Bearer(Guid userId)
        => Bearer(userId, null, null);

    private static AuthenticationHeaderValue Bearer(Guid userId, Guid? tenantId, Guid? userTenantId)
    {
        var options = Options.Create(new JwtOptions
        {
            Secret = JwtSecret,
            Issuer = "Shine",
            Audience = "Shine.Api",
            AccessTokenMinutes = 5
        });
        var service = new JwtAccessTokenService(options, new JwtSigningKeyRing(options));
        return new AuthenticationHeaderValue("Bearer", service.Create(userId, tenantId, userTenantId, Array.Empty<string>()).Token);
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
