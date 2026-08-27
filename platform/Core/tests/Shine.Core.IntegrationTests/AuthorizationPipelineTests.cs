using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BusinessCatalog.Domain;
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
using Scheduling.Domain;

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

    [Fact]
    public async Task Scheduling_queries_execute_the_real_read_permission_pipeline()
    {
        await using var db = fixture.CreateDb();
        var userWithoutRole = new User($"scheduling-no-read-{Guid.NewGuid():N}@example.test", "hash");
        var viewer = new User($"scheduling-viewer-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Scheduling read auth {Guid.NewGuid():N}");
        var unit = new Tenant($"Scheduling read unit {Guid.NewGuid():N}");
        var plan = new Shine.Domain.Plan($"SCHED-READ-{Guid.NewGuid():N}", "Scheduling read plan");
        plan.Modules.Add(new Shine.Domain.PlanModule(plan.Id, "SCHEDULING"));
        unit.AssignToCustomerAccount(account.Id);
        var noRoleMembership = new UserTenant(userWithoutRole.Id, unit.Id, userWithoutRole.Id, false);
        var viewerMembership = new UserTenant(viewer.Id, unit.Id, viewer.Id, false);
        db.AddRange(userWithoutRole, viewer, account, unit, plan,
            new Shine.Domain.TenantPlan(unit.Id, plan.Id),
            new CustomerAccountUser(account.Id, userWithoutRole.Id),
            new CustomerAccountUser(account.Id, viewer.Id), noRoleMembership, viewerMembership);
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, viewer.Id);
        var viewerRole = await db.CustomerAccountRoles.SingleAsync(x =>
            x.AccountId == account.Id && x.Name == CustomerAccountRole.ViewerName);
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(
            account.Id, viewer.Id, viewerRole.Id, allUnits: true, allModules: true));
        await db.SaveChangesAsync();

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        const string path = "/api/scheduling/settings";

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(userWithoutRole.Id, unit.Id, noRoleMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(viewer.Id, unit.Id, viewerMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Scheduling_http_pipeline_enforces_own_all_and_configure_without_revealing_other_agendas()
    {
        await using var db = fixture.CreateDb();
        var professionalUser = new User($"professional-http-{Guid.NewGuid():N}@example.test", "hash");
        var receptionUser = new User($"reception-http-{Guid.NewGuid():N}@example.test", "hash");
        var managerUser = new User($"manager-http-{Guid.NewGuid():N}@example.test", "hash");
        var unlinkedUser = new User($"unlinked-http-{Guid.NewGuid():N}@example.test", "hash");
        var configureOnlyUser = new User($"configure-only-http-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Scheduling scopes {Guid.NewGuid():N}");
        var unit = new Tenant($"Scheduling scopes unit {Guid.NewGuid():N}");
        var foreignAccount = new CustomerAccount($"Foreign scheduling scopes {Guid.NewGuid():N}");
        var foreignUnit = new Tenant($"Foreign scheduling scopes unit {Guid.NewGuid():N}");
        var plan = new Shine.Domain.Plan($"SCHED-SCOPES-{Guid.NewGuid():N}", "Scheduling scopes plan");
        plan.Modules.Add(new Shine.Domain.PlanModule(plan.Id, "SCHEDULING"));
        unit.AssignToCustomerAccount(account.Id);
        foreignUnit.AssignToCustomerAccount(foreignAccount.Id);
        var professionalMembership = new UserTenant(professionalUser.Id, unit.Id, professionalUser.Id, false);
        var receptionMembership = new UserTenant(receptionUser.Id, unit.Id, receptionUser.Id, false);
        var managerMembership = new UserTenant(managerUser.Id, unit.Id, managerUser.Id, false);
        var unlinkedMembership = new UserTenant(unlinkedUser.Id, unit.Id, unlinkedUser.Id, false);
        var configureOnlyMembership = new UserTenant(configureOnlyUser.Id, unit.Id, configureOnlyUser.Id, false);
        db.AddRange(professionalUser, receptionUser, managerUser, unlinkedUser, configureOnlyUser, account, unit,
            foreignAccount, foreignUnit, plan,
            new Shine.Domain.TenantPlan(unit.Id, plan.Id), professionalMembership, receptionMembership,
            managerMembership, unlinkedMembership, configureOnlyMembership,
            new CustomerAccountUser(account.Id, professionalUser.Id),
            new CustomerAccountUser(account.Id, receptionUser.Id),
            new CustomerAccountUser(account.Id, managerUser.Id),
            new CustomerAccountUser(account.Id, unlinkedUser.Id),
            new CustomerAccountUser(account.Id, configureOnlyUser.Id));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, managerUser.Id);
        var roles = await db.CustomerAccountRoles.Where(x => x.AccountId == account.Id).ToDictionaryAsync(x => x.Name);
        var configureOnlyRole = new CustomerAccountRole(account.Id, $"Configure only {Guid.NewGuid():N}");
        db.CustomerAccountRoles.Add(configureOnlyRole);
        var configurePermission = await db.Permissions.SingleAsync(x => x.Code == "scheduling.configure");
        db.CustomerAccountRolePermissions.Add(new CustomerAccountRolePermission(
            configureOnlyRole.Id, configurePermission.Id, PermissionScope.All));
        db.CustomerAccountUserRoles.AddRange(
            new CustomerAccountUserRole(account.Id, professionalUser.Id, roles[CustomerAccountRole.SchedulingProfessionalName].Id, true, true),
            new CustomerAccountUserRole(account.Id, receptionUser.Id, roles[CustomerAccountRole.SchedulingReceptionName].Id, true, true),
            new CustomerAccountUserRole(account.Id, managerUser.Id, roles[CustomerAccountRole.SchedulingManagerName].Id, true, true),
            new CustomerAccountUserRole(account.Id, unlinkedUser.Id, roles[CustomerAccountRole.SchedulingProfessionalName].Id, true, true),
            new CustomerAccountUserRole(account.Id, configureOnlyUser.Id, configureOnlyRole.Id, true, true));
        await db.SaveChangesAsync();

        var ownProfessional = new Professional(unit.Id, "Own professional", professionalUser.Id);
        var otherProfessional = new Professional(unit.Id, "Other professional");
        await using (var catalogDb = fixture.CreateBusinessCatalogDb())
        {
            catalogDb.Professionals.AddRange(ownProfessional, otherProfessional);
            await catalogDb.SaveChangesAsync();
        }

        var fromUtc = DateTime.UtcNow.AddDays(3);
        var ownAppointment = new Appointment(unit.Id, ownProfessional.Id, Guid.NewGuid(), "Own customer", "own@example.test", fromUtc, fromUtc.AddMinutes(30));
        var otherAppointment = new Appointment(unit.Id, otherProfessional.Id, Guid.NewGuid(), "Other customer", "other@example.test", fromUtc.AddHours(1), fromUtc.AddHours(1.5));
        var foreignAppointment = new Appointment(foreignUnit.Id, Guid.NewGuid(), Guid.NewGuid(), "Foreign customer", "foreign@example.test", fromUtc.AddHours(2), fromUtc.AddHours(2.5));
        await using (var schedulingDb = fixture.CreateSchedulingDb())
        {
            schedulingDb.Appointments.AddRange(ownAppointment, otherAppointment, foreignAppointment);
            await schedulingDb.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        var appointmentsPath = $"/api/scheduling/appointments?fromUtc={Uri.EscapeDataString(fromUtc.AddDays(-1).ToString("O"))}&toUtc={Uri.EscapeDataString(fromUtc.AddDays(1).ToString("O"))}&page=1&pageSize=20";

        client.DefaultRequestHeaders.Authorization = Bearer(professionalUser.Id, unit.Id, professionalMembership.UserTenantId);
        var ownResponse = await client.GetAsync(appointmentsPath);
        Assert.Equal(HttpStatusCode.OK, ownResponse.StatusCode);
        using (var json = JsonDocument.Parse(await ownResponse.Content.ReadAsStringAsync()))
        {
            var item = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(ownAppointment.Id, item.GetProperty("id").GetGuid());
        }
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/scheduling/professionals/{otherProfessional.Id}/availability")).StatusCode);
        var ownBlock = new { startsAtUtc = fromUtc.AddHours(3), endsAtUtc = fromUtc.AddHours(4), reason = "Own block" };
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/scheduling/professionals/{ownProfessional.Id}/blocks", ownBlock)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PostAsJsonAsync($"/api/scheduling/professionals/{otherProfessional.Id}/blocks", ownBlock)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(unlinkedUser.Id, unit.Id, unlinkedMembership.UserTenantId);
        var unlinked = await client.GetAsync(appointmentsPath);
        Assert.Equal(HttpStatusCode.Forbidden, unlinked.StatusCode);
        using (var json = JsonDocument.Parse(await unlinked.Content.ReadAsStringAsync()))
            Assert.Equal("PROFESSIONAL_CONTEXT_REQUIRED", json.RootElement.GetProperty("code").GetString());

        var settings = new
        {
            slotIntervalMinutes = 15,
            bufferBeforeMinutes = 0,
            bufferAfterMinutes = 0,
            timeZoneId = "UTC",
            conflictMode = 0,
            defaultMaxConcurrentAppointments = 1
        };
        client.DefaultRequestHeaders.Authorization = Bearer(receptionUser.Id, unit.Id, receptionMembership.UserTenantId);
        var receptionList = await client.GetAsync(appointmentsPath);
        Assert.Equal(HttpStatusCode.OK, receptionList.StatusCode);
        using (var json = JsonDocument.Parse(await receptionList.Content.ReadAsStringAsync()))
        {
            var ids = json.RootElement.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToArray();
            Assert.Equal(2, ids.Length);
            Assert.Contains(ownAppointment.Id, ids);
            Assert.Contains(otherAppointment.Id, ids);
            Assert.DoesNotContain(foreignAppointment.Id, ids);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync("/api/scheduling/settings", settings)).StatusCode);
        var receptionBlock = new { startsAtUtc = fromUtc.AddHours(5), endsAtUtc = fromUtc.AddHours(6), reason = "Reception block" };
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/scheduling/professionals/{otherProfessional.Id}/blocks", receptionBlock)).StatusCode);
        var recurringAvailability = new { dayOfWeek = 1, startsAt = "09:00:00", endsAt = "17:00:00" };
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"/api/scheduling/professionals/{otherProfessional.Id}/availability", recurringAvailability)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(managerUser.Id, unit.Id, managerMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/scheduling/settings", settings)).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync($"/api/scheduling/professionals/{otherProfessional.Id}/availability", recurringAvailability)).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(configureOnlyUser.Id, unit.Id, configureOnlyMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/scheduling/settings", settings)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(appointmentsPath)).StatusCode);
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
