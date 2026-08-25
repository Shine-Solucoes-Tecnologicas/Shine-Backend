using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class BusinessCatalogApiTests(DatabaseFixture fixture)
{
    private const string JwtSecret = "business-catalog-tests-secret-at-least-32-bytes";

    [Fact]
    public async Task Crud_filters_soft_deletion_and_legacy_route_execute_through_http()
    {
        var access = await CreateAccessAsync();
        await using var factory = new ApiFactory(fixture.ConnectionString, JwtSecret);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/business-catalog/professionals")).StatusCode);
        client.DefaultRequestHeaders.Authorization = Bearer(access.Editor.Id, access.Unit.Id, access.EditorMembership.UserTenantId);

        var created = await client.PostAsJsonAsync("/api/v1/business-catalog/professionals", new { name = "Ana Silva", userId = (Guid?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var professionalId = (await Json(created)).GetProperty("id").GetGuid();

        var updated = await client.PutAsJsonAsync($"/api/v1/business-catalog/professionals/{professionalId}", new { name = "Ana Souza", userId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Equal("Ana Souza", (await Json(updated)).GetProperty("name").GetString());

        var filtered = await client.GetAsync("/api/v1/business-catalog/professionals?search=Souza&isActive=true");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        Assert.Single((await Json(filtered)).GetProperty("items").EnumerateArray());

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/business-catalog/professionals/{professionalId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/business-catalog/professionals/{professionalId}")).StatusCode);
        Assert.False((await Json(await client.GetAsync($"/api/v1/business-catalog/professionals/{professionalId}"))).GetProperty("isActive").GetBoolean());

        var legacy = await client.GetAsync("/api/business-catalog/professionals");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        Assert.Equal("true", legacy.Headers.GetValues("Deprecation").Single());
        Assert.Contains("/api/v1/business-catalog", legacy.Headers.GetValues("Link").Single());
    }

    [Fact]
    public async Task Authorization_tenant_isolation_and_association_idempotency_execute_through_http()
    {
        var access = await CreateAccessAsync();
        var foreignUnit = new Tenant($"Foreign catalog {Guid.NewGuid():N}");
        foreignUnit.AssignToCustomerAccount(access.Account.Id);
        var foreignMembership = new UserTenant(access.Editor.Id, foreignUnit.Id, access.Editor.Id, false);
        await using (var coreDb = fixture.CreateDb())
        {
            coreDb.AddRange(foreignUnit, foreignMembership);
            await coreDb.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(fixture.ConnectionString, JwtSecret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(access.Viewer.Id, access.Unit.Id, access.ViewerMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/business-catalog/services", new { name = "Denied", durationMinutes = 30 })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/business-catalog/services")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(access.Editor.Id, access.Unit.Id, access.EditorMembership.UserTenantId);
        var professionalId = (await Json(await client.PostAsJsonAsync("/api/v1/business-catalog/professionals", new { name = $"Professional {Guid.NewGuid():N}", userId = (Guid?)null }))).GetProperty("id").GetGuid();
        var serviceId = (await Json(await client.PostAsJsonAsync("/api/v1/business-catalog/services", new { name = $"Service {Guid.NewGuid():N}", durationMinutes = 45 }))).GetProperty("id").GetGuid();
        var associationPath = $"/api/v1/business-catalog/professionals/{professionalId}/services/{serviceId}";

        var associations = await Task.WhenAll(client.PutAsync(associationPath, null), client.PutAsync(associationPath, null));
        Assert.All(associations, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(associationPath)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(associationPath)).StatusCode);
        await using (var catalog = fixture.CreateBusinessCatalogDb())
            Assert.Single(await catalog.ProfessionalServices.Where(x => x.TenantId == access.Unit.Id && x.ProfessionalId == professionalId && x.ServiceId == serviceId).ToArrayAsync());

        client.DefaultRequestHeaders.Authorization = Bearer(access.Editor.Id, foreignUnit.Id, foreignMembership.UserTenantId);
        var isolated = await client.GetAsync($"/api/v1/business-catalog/professionals/{professionalId}");
        Assert.Equal(HttpStatusCode.NotFound, isolated.StatusCode);
        Assert.Equal("PROFESSIONAL_NOT_FOUND", (await Json(isolated)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Professional_user_must_have_an_active_membership_in_the_same_unit()
    {
        var access = await CreateAccessAsync();
        var foreignUser = new User($"catalog-foreign-{Guid.NewGuid():N}@example.test", "hash");
        var inactiveUser = new User($"catalog-inactive-{Guid.NewGuid():N}@example.test", "hash");
        var foreignUnit = new Tenant($"Foreign catalog {Guid.NewGuid():N}");
        foreignUnit.AssignToCustomerAccount(access.Account.Id);
        var foreignMembership = new UserTenant(foreignUser.Id, foreignUnit.Id, access.Editor.Id, false);
        var inactiveMembership = new UserTenant(inactiveUser.Id, access.Unit.Id, access.Editor.Id, false);
        inactiveMembership.Deactivate();
        await using (var coreDb = fixture.CreateDb())
        {
            coreDb.AddRange(foreignUser, inactiveUser, foreignUnit, foreignMembership, inactiveMembership);
            await coreDb.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(fixture.ConnectionString, JwtSecret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(access.Editor.Id, access.Unit.Id, access.EditorMembership.UserTenantId);

        var valid = await client.PostAsJsonAsync("/api/v1/business-catalog/professionals", new
        {
            name = $"Linked {Guid.NewGuid():N}",
            userId = access.Editor.Id
        });
        Assert.Equal(HttpStatusCode.Created, valid.StatusCode);
        var professionalId = (await Json(valid)).GetProperty("id").GetGuid();

        var foreign = await client.PostAsJsonAsync("/api/v1/business-catalog/professionals", new
        {
            name = $"Foreign {Guid.NewGuid():N}",
            userId = foreignUser.Id
        });
        Assert.Equal(HttpStatusCode.BadRequest, foreign.StatusCode);
        Assert.Equal("USER_NOT_AVAILABLE_FOR_UNIT", (await Json(foreign)).GetProperty("code").GetString());

        var inactive = await client.PutAsJsonAsync($"/api/v1/business-catalog/professionals/{professionalId}", new
        {
            name = $"Inactive {Guid.NewGuid():N}",
            userId = inactiveUser.Id
        });
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal("USER_NOT_AVAILABLE_FOR_UNIT", (await Json(inactive)).GetProperty("code").GetString());

        var unchanged = await client.GetAsync($"/api/v1/business-catalog/professionals/{professionalId}");
        Assert.Equal(access.Editor.Id, (await Json(unchanged)).GetProperty("userId").GetGuid());
    }

    private async Task<AccessData> CreateAccessAsync()
    {
        var editor = new User($"catalog-editor-{Guid.NewGuid():N}@example.test", "hash");
        var viewer = new User($"catalog-viewer-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Catalog account {Guid.NewGuid():N}");
        var unit = new Tenant($"Catalog unit {Guid.NewGuid():N}");
        unit.AssignToCustomerAccount(account.Id);
        var editorMembership = new UserTenant(editor.Id, unit.Id, editor.Id, false);
        var viewerMembership = new UserTenant(viewer.Id, unit.Id, viewer.Id, false);
        await using var db = fixture.CreateDb();
        db.AddRange(editor, viewer, account, unit, editorMembership, viewerMembership,
            new CustomerAccountUser(account.Id, editor.Id), new CustomerAccountUser(account.Id, viewer.Id));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, editor.Id);
        var roles = await db.CustomerAccountRoles.Where(x => x.AccountId == account.Id).ToDictionaryAsync(x => x.Name);
        db.CustomerAccountUserRoles.AddRange(
            new CustomerAccountUserRole(account.Id, editor.Id, roles[CustomerAccountRole.EditorName].Id, allUnits: true, allModules: true),
            new CustomerAccountUserRole(account.Id, viewer.Id, roles[CustomerAccountRole.ViewerName].Id, allUnits: true, allModules: true));
        await db.SaveChangesAsync();
        return new AccessData(editor, viewer, account, unit, editorMembership, viewerMembership);
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private static AuthenticationHeaderValue Bearer(Guid userId, Guid tenantId, Guid userTenantId)
    {
        var options = Options.Create(new JwtOptions { Secret = JwtSecret, Issuer = "Shine", Audience = "Shine.Api", AccessTokenMinutes = 5 });
        var service = new JwtAccessTokenService(options, new JwtSigningKeyRing(options));
        return new AuthenticationHeaderValue("Bearer", service.Create(userId, tenantId, userTenantId, []).Token);
    }

    private sealed record AccessData(User Editor, User Viewer, CustomerAccount Account, Tenant Unit, UserTenant EditorMembership, UserTenant ViewerMembership);

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
