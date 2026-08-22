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
using Shine.Api.Controllers;
using Shine.Application;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;
using Scheduling.Domain;

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

        var invalid = await client.PostAsJsonAsync("/api/v1/customers", new
        {
            name = "Sensitive invalid customer",
            email = "victim@invalid",
            taxIdentifier = "123.456"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Sensitive invalid customer", invalidBody, StringComparison.Ordinal);
        Assert.DoesNotContain("victim@invalid", invalidBody, StringComparison.Ordinal);
        Assert.DoesNotContain("123.456", invalidBody, StringComparison.Ordinal);

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

    [Fact]
    public async Task Customer_endpoints_deny_financial_role_and_unit_scope_crossing()
    {
        var setup = await SetupAsync();
        var siblingTenant = new Tenant($"Sibling customer API unit {Guid.NewGuid():N}");
        siblingTenant.AssignToCustomerAccount(setup.Account.Id);
        var financial = new User($"customer-financial-{Guid.NewGuid():N}@example.test", "hash");
        var scopedViewer = new User($"customer-scoped-viewer-{Guid.NewGuid():N}@example.test", "hash");
        var financialMembership = new UserTenant(financial.Id, setup.Tenant.Id, financial.Id, false);
        var scopedMembership = new UserTenant(scopedViewer.Id, setup.Tenant.Id, scopedViewer.Id, false);
        var scopedSiblingMembership = new UserTenant(scopedViewer.Id, siblingTenant.Id, scopedViewer.Id, false);
        var localCustomer = new Customer(setup.Tenant.Id, "Local customer");
        var siblingCustomer = new Customer(siblingTenant.Id, "Sibling customer");

        await using (var db = fixture.CreateDb())
        {
            db.AddRange(siblingTenant, financial, scopedViewer, financialMembership, scopedMembership,
                scopedSiblingMembership, localCustomer, siblingCustomer,
                new CustomerAccountUser(setup.Account.Id, financial.Id),
                new CustomerAccountUser(setup.Account.Id, scopedViewer.Id));
            await db.SaveChangesAsync();

            var financialRole = await db.CustomerAccountRoles.SingleAsync(x =>
                x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.FinancialName);
            var viewerRole = await db.CustomerAccountRoles.SingleAsync(x =>
                x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.ViewerName);
            db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(
                setup.Account.Id, financial.Id, financialRole.Id, allUnits: true, allModules: true));
            var scopedRole = new CustomerAccountUserRole(
                setup.Account.Id, scopedViewer.Id, viewerRole.Id, allUnits: false, allModules: true);
            scopedRole.ReplaceScope(false, true, [setup.Tenant.Id], []);
            db.CustomerAccountUserRoles.Add(scopedRole);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(
            financial.Id, setup.Tenant.Id, financialMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/customers", new { name = "Financial denied" })).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(
            scopedViewer.Id, setup.Tenant.Id, scopedMembership.UserTenantId);
        var localPage = await client.GetFromJsonAsync<PagedResponse<CustomerDetails>>("/api/v1/customers");
        Assert.Contains(localPage!.Items, x => x.Id == localCustomer.Id);
        Assert.DoesNotContain(localPage.Items, x => x.Id == siblingCustomer.Id);

        client.DefaultRequestHeaders.Authorization = Bearer(
            scopedViewer.Id, siblingTenant.Id, scopedSiblingMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/customers")).StatusCode);
    }

    [Fact]
    public async Task Customer_list_supports_normalized_search_state_paging_sorting_and_isolation()
    {
        var setup = await SetupAsync();
        var alice = new Customer(setup.Tenant.Id, "Alice Alpha", "alice@example.test", "+55 11 90000-0001");
        var alicia = new Customer(setup.Tenant.Id, "Alicia Beta", "alicia@example.test", "+55 21 90000-0002");
        var inactive = new Customer(setup.Tenant.Id, "Bob Inactive", "bob@example.test", "+55 31 90000-0003");
        var foreign = new Customer(setup.OtherTenant.Id, "Alice Foreign", "alice.foreign@example.test", "+55 11 90000-0004");
        inactive.Deactivate(DateTime.UtcNow);

        await using (var seed = fixture.CreateDb())
        {
            seed.Customers.AddRange(alice, alicia, inactive, foreign);
            await seed.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(setup.Viewer.Id, setup.Tenant.Id, setup.ViewerMembership.UserTenantId);

        var firstPage = await client.GetFromJsonAsync<PagedResponse<CustomerDetails>>(
            "/api/v1/customers?search=ali&page=1&pageSize=1&sortBy=name");
        Assert.NotNull(firstPage);
        Assert.Equal(2, firstPage.TotalItems);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(alice.Id, Assert.Single(firstPage.Items).Id);

        var secondPage = await client.GetFromJsonAsync<PagedResponse<CustomerDetails>>(
            "/api/v1/customers?search=ali&page=2&pageSize=1&sortBy=name");
        Assert.Equal(alicia.Id, Assert.Single(secondPage!.Items).Id);

        var byEmail = await client.GetFromJsonAsync<PagedResponse<CustomerDetails>>(
            "/api/v1/customers?search=alice%40example.test");
        Assert.Equal(alice.Id, Assert.Single(byEmail!.Items).Id);

        var byPhone = await client.GetFromJsonAsync<PagedResponse<CustomerDetails>>(
            "/api/v1/customers?search=%2B55%2011%2090000");
        Assert.Equal(alice.Id, Assert.Single(byPhone!.Items).Id);

        var inactivePage = await client.GetFromJsonAsync<PagedResponse<CustomerDetails>>(
            "/api/v1/customers?isActive=false&search=bob");
        var inactiveItem = Assert.Single(inactivePage!.Items);
        Assert.Equal(inactive.Id, inactiveItem.Id);
        Assert.False(inactiveItem.IsActive);

        var invalidSort = await client.GetAsync("/api/v1/customers?sortBy=tenantId");
        Assert.Equal(HttpStatusCode.BadRequest, invalidSort.StatusCode);
        var error = await invalidSort.Content.ReadFromJsonAsync<CustomerErrorResponse>();
        Assert.Equal("customer_sort_invalid", Assert.Single(error!.Errors).Code);
    }

    [Fact]
    public async Task Customer_history_is_paged_minimized_authorized_and_tenant_isolated()
    {
        var setup = await SetupAsync();
        var customer = new Customer(setup.Tenant.Id, "History customer", "sensitive@example.test", "+55 11 99999-0000");
        var emptyCustomer = new Customer(setup.Tenant.Id, "Empty history");
        var inactiveCustomer = new Customer(setup.Tenant.Id, "Inactive history");
        var foreignCustomer = new Customer(setup.OtherTenant.Id, "Foreign history");
        inactiveCustomer.Deactivate(DateTime.UtcNow);
        var customerOnlyUser = new User($"customer-history-customer-only-{Guid.NewGuid():N}@example.test", "hash");
        var customerOnlyMembership = new UserTenant(customerOnlyUser.Id, setup.Tenant.Id, customerOnlyUser.Id, false);
        await using (var core = fixture.CreateDb())
        {
            var viewerRole = await core.CustomerAccountRoles.SingleAsync(x =>
                x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.ViewerName);
            core.AddRange(customer, emptyCustomer, inactiveCustomer, foreignCustomer, customerOnlyUser,
                customerOnlyMembership, new CustomerAccountUser(setup.Account.Id, customerOnlyUser.Id),
                new CustomerAccountUserRole(setup.Account.Id, customerOnlyUser.Id, viewerRole.Id, allUnits: true, allModules: false));
            await core.SaveChangesAsync();
        }

        var professional = new Professional(setup.Tenant.Id, $"History professional {Guid.NewGuid():N}");
        var service = new Service(setup.Tenant.Id, $"History service {Guid.NewGuid():N}", 30);
        var foreignProfessional = new Professional(setup.OtherTenant.Id, $"Foreign professional {Guid.NewGuid():N}");
        var foreignService = new Service(setup.OtherTenant.Id, $"Foreign service {Guid.NewGuid():N}", 30);
        var olderStart = DateTime.UtcNow.AddDays(-2);
        var latestStart = DateTime.UtcNow.AddDays(-1);
        var older = new Appointment(setup.Tenant.Id, professional.Id, service.Id, "Old snapshot", "old-secret",
            olderStart, olderStart.AddMinutes(30), customer.Id);
        var latest = new Appointment(setup.Tenant.Id, professional.Id, service.Id, "Latest snapshot", "latest-secret",
            latestStart, latestStart.AddMinutes(30), customer.Id);
        var foreign = new Appointment(setup.OtherTenant.Id, foreignProfessional.Id, foreignService.Id, "Foreign snapshot", "foreign-secret",
            latestStart.AddHours(1), latestStart.AddHours(1).AddMinutes(30), foreignCustomer.Id);
        await using (var scheduling = fixture.CreateSchedulingDb())
        {
            scheduling.AddRange(professional, service, foreignProfessional, foreignService, older, latest, foreign);
            await scheduling.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString(), JwtSecret);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(customerOnlyUser.Id, setup.Tenant.Id, customerOnlyMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/customers/{customer.Id}/history")).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(setup.Viewer.Id, setup.Tenant.Id, setup.ViewerMembership.UserTenantId);
        var firstResponse = await client.GetAsync($"/api/v1/customers/{customer.Id}/history?page=1&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var json = await firstResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("snapshot", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sensitive@example.test", json, StringComparison.OrdinalIgnoreCase);
        var first = JsonSerializer.Deserialize<CustomerHistoryResponse>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(first);
        Assert.Equal("1.0", first.SchemaVersion);
        Assert.Equal(2, first.TotalItems);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal(latest.Id, Assert.Single(first.Items).SourceId);
        Assert.Equal("scheduling.appointment", Assert.Single(first.Items).Type);

        var second = await client.GetFromJsonAsync<CustomerHistoryResponse>(
            $"/api/v1/customers/{customer.Id}/history?page=2&pageSize=1");
        Assert.Equal(older.Id, Assert.Single(second!.Items).SourceId);

        var empty = await client.GetFromJsonAsync<CustomerHistoryResponse>(
            $"/api/v1/customers/{emptyCustomer.Id}/history");
        Assert.Empty(empty!.Items);
        Assert.Equal(0, empty.TotalItems);

        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/customers/{inactiveCustomer.Id}/history")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/api/v1/customers/{foreignCustomer.Id}/history")).StatusCode);
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
        var plan = new Plan($"CUSTOMER-API-{Guid.NewGuid():N}", "Customer API plan");
        plan.Modules.Add(new PlanModule(plan.Id, "SCHEDULING"));
        var editorMembership = new UserTenant(editor.Id, tenant.Id, editor.Id, false);
        var viewerMembership = new UserTenant(viewer.Id, tenant.Id, viewer.Id, false);

        await using var db = fixture.CreateDb();
        db.AddRange(account, otherAccount, tenant, otherTenant, editor, viewer, plan, new TenantPlan(tenant.Id, plan.Id), editorMembership, viewerMembership,
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
