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
using Shine.Application;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class CustomerApiTests(DatabaseFixture fixture)
{
    private const string JwtSecret = "customer-api-tests-only-secret-with-at-least-32-bytes";

    [Fact]
    public async Task Customer_lifecycle_is_tenant_isolated_validated_and_audited()
    {
        var setup = await SetupAsync();
        var foreignCustomer = new Customer(setup.OtherTenant.Id, "Foreign");
        await using (var seed = fixture.CreateDb())
        {
            seed.Customers.Add(foreignCustomer);
            await seed.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(setup.Editor.Id, setup.Tenant.Id, setup.EditorMembership.UserTenantId);

        var invalid = await client.PostAsJsonAsync("/api/v1/customers", new { name = "", email = "invalid" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var create = await client.PostAsJsonAsync("/api/v1/customers", new
        {
            name = "  API Customer  ",
            email = "CUSTOMER@EXAMPLE.TEST",
            phone = "+55 (11) 98888-7777",
            taxIdentifier = "321.654.987-00"
        });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var created = await create.Content.ReadFromJsonAsync<CustomerDetails>();
        Assert.NotNull(created);
        Assert.Equal(setup.Tenant.Id, created.TenantId);
        Assert.Equal("customer@example.test", created.Email);

        var duplicate = await client.PostAsJsonAsync("/api/v1/customers", new
        {
            name = "Duplicate",
            taxIdentifier = "32165498700"
        });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/customers/{foreignCustomer.Id}")).StatusCode);

        var update = await client.PutAsJsonAsync($"/api/v1/customers/{created.Id}", new
        {
            name = "Updated Customer",
            email = "updated@example.test",
            phone = "11977776666",
            taxIdentifier = "32165498700"
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/v1/customers/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/customers/{created.Id}")).StatusCode);

        var reactivate = await client.PostAsync($"/api/v1/customers/{created.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/v1/customers/{created.Id}")).StatusCode);

        await using var verification = fixture.CreateDb();
        var actions = await verification.AuditEntries
            .Where(x => x.EntityType == nameof(Customer) && x.EntityId == created.Id.ToString())
            .Select(x => x.Action)
            .ToArrayAsync();
        Assert.Contains("CREATE", actions);
        Assert.True(actions.Count(action => action == "UPDATE") >= 3);
    }

    [Fact]
    public async Task Customer_endpoints_enforce_authentication_and_accumulated_customer_roles()
    {
        var setup = await SetupAsync();
        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/v1/customers/{Guid.NewGuid()}")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(setup.Viewer.Id, setup.Tenant.Id, setup.ViewerMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/customers/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/customers", new { name = "Denied" })).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(setup.Editor.Id, setup.Tenant.Id, setup.EditorMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/customers", new { name = "Allowed" })).StatusCode);
    }

    private async Task<Setup> SetupAsync()
    {
        var account = new CustomerAccount($"Customer API {Guid.NewGuid():N}");
        var otherAccount = new CustomerAccount($"Other customer API {Guid.NewGuid():N}");
        var tenant = new Tenant($"Customer API unit {Guid.NewGuid():N}");
        var otherTenant = new Tenant($"Other customer API unit {Guid.NewGuid():N}");
        tenant.AssignToCustomerAccount(account.Id);
        otherTenant.AssignToCustomerAccount(otherAccount.Id);
        var editor = new User($"customer-editor-{Guid.NewGuid():N}@example.test", "hash");
        var viewer = new User($"customer-viewer-{Guid.NewGuid():N}@example.test", "hash");
        var editorMembership = new UserTenant(editor.Id, tenant.Id, editor.Id, false);
        var viewerMembership = new UserTenant(viewer.Id, tenant.Id, viewer.Id, false);

        await using var db = fixture.CreateDb();
        db.AddRange(account, otherAccount, tenant, otherTenant, editor, viewer, editorMembership, viewerMembership,
            new CustomerAccountUser(account.Id, editor.Id), new CustomerAccountUser(account.Id, viewer.Id));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, editor.Id);

        var editorRole = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == account.Id && x.Name == CustomerAccountRole.EditorName);
        var viewerRole = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == account.Id && x.Name == CustomerAccountRole.ViewerName);
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(account.Id, editor.Id, editorRole.Id, allUnits: true, allModules: true));
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(account.Id, viewer.Id, viewerRole.Id, allUnits: true, allModules: true));
        await db.SaveChangesAsync();

        return new(account, tenant, otherTenant, editor, viewer, editorMembership, viewerMembership);
    }

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

    private static string ConnectionString() => Environment.GetEnvironmentVariable("ConnectionStrings__ShineDb")
        ?? "Host=localhost;Port=5433;Database=shine;Username=shine;Password=shine";

    private sealed class ApiFactory(string connectionString, string jwtSecret) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ShineDb", connectionString);
            builder.UseSetting("Jwt:Secret", jwtSecret);
            builder.UseSetting("Jwt:Issuer", "Shine");
            builder.UseSetting("Jwt:Audience", "Shine.Api");
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddJsonConsole();
            });
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }

    private sealed record Setup(
        CustomerAccount Account,
        Tenant Tenant,
        Tenant OtherTenant,
        User Editor,
        User Viewer,
        UserTenant EditorMembership,
        UserTenant ViewerMembership);
}
