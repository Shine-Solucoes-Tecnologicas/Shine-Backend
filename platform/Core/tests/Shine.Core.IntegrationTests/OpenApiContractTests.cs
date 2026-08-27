using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Swashbuckle.AspNetCore.Swagger;

namespace Shine.Core.IntegrationTests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task Document_describes_routes_models_errors_pagination_and_bearer_security()
    {
        using var factory = new ApiFactory("Development");
        using var scope = factory.Services.CreateScope();
        _ = scope.ServiceProvider.GetRequiredService<ISwaggerProvider>().GetSwagger("v1");
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/openapi/v1.json");

        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = document.RootElement;
        Assert.StartsWith("3.", root.GetProperty("openapi").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());

        var paths = root.GetProperty("paths");
        var login = paths.GetProperty("/api/auth/login").GetProperty("post");
        Assert.Empty(login.GetProperty("security").EnumerateArray());

        var customers = paths.GetProperty("/api/v1/customers").GetProperty("get");
        var responses = customers.GetProperty("responses");
        Assert.True(responses.TryGetProperty("401", out _));
        Assert.True(responses.TryGetProperty("403", out _));

        var components = root.GetProperty("components");
        var bearer = components.GetProperty("securitySchemes").GetProperty("Bearer");
        Assert.Equal("http", bearer.GetProperty("type").GetString());
        Assert.Equal("bearer", bearer.GetProperty("scheme").GetString());

        var schemas = components.GetProperty("schemas").EnumerateObject().Select(item => item.Name).ToArray();
        Assert.Contains(schemas, name => name.EndsWith("CustomerErrorResponse", StringComparison.Ordinal));
        Assert.Contains(schemas, name => name.EndsWith("PagedResponse", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Interactive_documentation_is_available_only_in_development()
    {
        using var developmentFactory = new ApiFactory("Development");
        using var developmentClient = developmentFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        using var developmentResponse = await developmentClient.GetAsync("/docs/index.html");
        developmentResponse.EnsureSuccessStatusCode();

        using var stagingFactory = new ApiFactory("Staging");
        using var stagingClient = stagingFactory.CreateClient();
        using var stagingResponse = await stagingClient.GetAsync("/docs/index.html");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, stagingResponse.StatusCode);

        using var contractResponse = await stagingClient.GetAsync("/openapi/v1.json");
        contractResponse.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Administrative_operations_publish_permissions_effects_and_stable_metadata()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var administrativeOperations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/admin/", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(operation => (Path: path.Name, Method: operation.Name, Contract: operation.Value)))
            .ToArray();

        Assert.Equal(23, administrativeOperations.Length);
        foreach (var operation in administrativeOperations)
        {
            Assert.StartsWith("Admin_", operation.Contract.GetProperty("operationId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.Contract.GetProperty("summary").GetString()));
            var description = operation.Contract.GetProperty("description").GetString();
            Assert.Contains("Permissões globais:", description);
            Assert.Contains("Efeito operacional:", description);
            Assert.NotEmpty(operation.Contract.GetProperty("tags").EnumerateArray());
            Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("401", out _));
            Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("403", out _));
        }

        var suspend = administrativeOperations.Single(operation =>
            operation.Path == "/api/admin/tenants/{tenantId}/suspend" && operation.Method == "put").Contract;
        Assert.Contains("admin.manage", suspend.GetProperty("description").GetString());
        Assert.Contains("revoga as sessões", suspend.GetProperty("description").GetString());

        var audit = administrativeOperations.Single(operation =>
            operation.Path == "/api/admin/audit" && operation.Method == "get").Contract;
        Assert.Contains("admin.audit", audit.GetProperty("description").GetString());

        var createContract = administrativeOperations.Single(operation =>
            operation.Path == "/api/admin/billing/contracts" && operation.Method == "post").Contract;
        Assert.Contains("billing.commercial.read", createContract.GetProperty("description").GetString());
        Assert.Contains("billing.commercial.manage", createContract.GetProperty("description").GetString());
        Assert.True(createContract.GetProperty("responses").TryGetProperty("409", out _));
    }

    [Fact]
    public async Task Identity_and_access_operations_publish_context_and_stable_metadata()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var operations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/auth", StringComparison.Ordinal) ||
                path.Name.StartsWith("/api/tenants", StringComparison.Ordinal) ||
                path.Name.StartsWith("/api/account/access", StringComparison.Ordinal) ||
                path.Name.StartsWith("/api/modules", StringComparison.Ordinal) ||
                path.Name.StartsWith("/api/plans", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(operation => (Path: path.Name, Method: operation.Name, Contract: operation.Value)))
            .ToArray();

        Assert.Equal(34, operations.Length);
        foreach (var operation in operations)
        {
            Assert.StartsWith("Access_", operation.Contract.GetProperty("operationId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.Contract.GetProperty("summary").GetString()));
            var description = operation.Contract.GetProperty("description").GetString();
            Assert.Contains("Acesso exigido:", description);
            Assert.Contains("Efeito operacional:", description);
        }

        var register = operations.Single(operation => operation.Path == "/api/auth/register").Contract;
        Assert.Empty(register.GetProperty("security").EnumerateArray());

        var assignRole = operations.Single(operation =>
            operation.Path == "/api/account/access/users/{userId}/roles/{roleId}" && operation.Method == "put").Contract;
        Assert.Contains("account.access.manage", assignRole.GetProperty("description").GetString());
        Assert.Contains("impedindo escalada", assignRole.GetProperty("description").GetString());
        Assert.True(assignRole.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(assignRole.GetProperty("responses").TryGetProperty("403", out _));
    }

    [Fact]
    public async Task Billing_operations_publish_access_effects_idempotency_and_errors()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var operations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/account/billing", StringComparison.Ordinal) ||
                path.Name.StartsWith("/api/admin/billing", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(operation => (Path: path.Name, Method: operation.Name, Contract: operation.Value)))
            .ToArray();

        Assert.Equal(12, operations.Length);
        foreach (var operation in operations)
        {
            var operationId = operation.Contract.GetProperty("operationId").GetString();
            Assert.True(operationId?.StartsWith("Billing_", StringComparison.Ordinal) == true ||
                operationId?.StartsWith("Admin_", StringComparison.Ordinal) == true);
            Assert.False(string.IsNullOrWhiteSpace(operation.Contract.GetProperty("summary").GetString()));
            Assert.Contains("Efeito operacional:", operation.Contract.GetProperty("description").GetString());
            Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("401", out _));
            Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("403", out _));
        }

        var checkout = operations.Single(operation =>
            operation.Path == "/api/account/billing/subscriptions/checkout" && operation.Method == "post").Contract;
        var checkoutDescription = checkout.GetProperty("description").GetString();
        Assert.Contains("subscriptions.manage", checkoutDescription);
        Assert.Contains("idempotência", checkoutDescription);
        Assert.True(checkout.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(checkout.GetProperty("responses").TryGetProperty("409", out _));
        Assert.True(checkout.GetProperty("responses").TryGetProperty("503", out _));

        var customerContracts = operations.Single(operation =>
            operation.Path == "/api/account/billing/contracts" && operation.Method == "get").Contract;
        Assert.Contains("ocultando autoria", customerContracts.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Business_catalog_publishes_versioned_and_deprecated_contracts()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var operations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/v1/business-catalog", StringComparison.Ordinal) ||
                path.Name.StartsWith("/api/business-catalog", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(operation => (Path: path.Name, Method: operation.Name, Contract: operation.Value)))
            .ToArray();

        Assert.Equal(30, operations.Length);
        Assert.Equal(30, operations.Select(operation => operation.Contract.GetProperty("operationId").GetString()).Distinct().Count());
        foreach (var operation in operations)
        {
            Assert.StartsWith("BusinessCatalog_", operation.Contract.GetProperty("operationId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.Contract.GetProperty("summary").GetString()));
            var description = operation.Contract.GetProperty("description").GetString();
            Assert.Contains("business-catalog.", description);
            Assert.Contains("Efeito operacional:", description);
            Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("401", out _));
            Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("403", out _));

            var legacy = operation.Path.StartsWith("/api/business-catalog", StringComparison.Ordinal);
            Assert.Equal(legacy, operation.Contract.TryGetProperty("deprecated", out var deprecated) && deprecated.GetBoolean());
            Assert.Contains(legacy ? "rota legada descontinuada" : "rota canônica versionada", description);
        }

        var createProfessional = operations.Single(operation =>
            operation.Path == "/api/v1/business-catalog/professionals" && operation.Method == "post").Contract;
        Assert.Contains("desacoplado da agenda", createProfessional.GetProperty("description").GetString());
        Assert.True(createProfessional.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(createProfessional.GetProperty("responses").TryGetProperty("409", out _));

        var associate = operations.Single(operation =>
            operation.Path == "/api/v1/business-catalog/professionals/{professionalId}/services/{serviceId}" &&
            operation.Method == "put").Contract;
        Assert.Contains("idempotente", associate.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Scheduling_contract_covers_authenticated_public_limits_and_concurrency()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var operations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Where(path => path.Name.StartsWith("/api/scheduling", StringComparison.Ordinal) ||
                path.Name.StartsWith("/api/public/scheduling", StringComparison.Ordinal))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(operation => (Path: path.Name, Method: operation.Name, Contract: operation.Value)))
            .ToArray();

        Assert.Equal(19, operations.Length);
        Assert.Equal(19, operations.Select(operation => operation.Contract.GetProperty("operationId").GetString()).Distinct().Count());
        foreach (var operation in operations)
        {
            Assert.StartsWith("Scheduling_", operation.Contract.GetProperty("operationId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.Contract.GetProperty("summary").GetString()));
            var description = operation.Contract.GetProperty("description").GetString();
            Assert.Contains("SCHEDULING", description);
            Assert.Contains("Efeito operacional:", description);

            var isPublic = operation.Path.StartsWith("/api/public/scheduling", StringComparison.Ordinal);
            if (isPublic)
            {
                Assert.True(operation.Contract.TryGetProperty("security", out var publicSecurity));
                Assert.Empty(publicSecurity.EnumerateArray());
                Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("429", out _));
            }
            else
            {
                Assert.False(operation.Contract.TryGetProperty("security", out var protectedSecurity) &&
                    !protectedSecurity.EnumerateArray().Any());
                Assert.Contains("scheduling.", description);
                Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("401", out _));
                Assert.True(operation.Contract.GetProperty("responses").TryGetProperty("403", out _));
            }
        }

        var appointments = operations.Single(operation =>
            operation.Path == "/api/scheduling/appointments" && operation.Method == "get").Contract;
        Assert.Contains("scheduling.read", appointments.GetProperty("description").GetString());

        var create = operations.Single(operation =>
            operation.Path == "/api/scheduling/appointments" && operation.Method == "post").Contract;
        Assert.Contains("entitlement", create.GetProperty("description").GetString());
        Assert.True(create.GetProperty("responses").TryGetProperty("409", out _));

        var reschedule = operations.Single(operation =>
            operation.Path == "/api/scheduling/appointments/{appointmentId}/reschedule" && operation.Method == "put").Contract;
        Assert.Contains("ExpectedVersion", reschedule.GetProperty("description").GetString());

        var publicCreate = operations.Single(operation =>
            operation.Path == "/api/public/scheduling/{tenantId}/appointments" && operation.Method == "post").Contract;
        Assert.Contains("IP efetivo + unidade", publicCreate.GetProperty("description").GetString());
    }

    [Fact]
    public async Task Auxiliary_capabilities_publish_isolation_permissions_and_health_contracts()
    {
        using var factory = new ApiFactory("Development");
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var prefixes = new[] { "/api/v1/customers", "/api/files", "/api/notifications", "/api/dashboard", "/api/settings", "/api/feature-flags", "/api/health" };
        var operations = document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .Where(path => prefixes.Any(prefix => path.Name.StartsWith(prefix, StringComparison.Ordinal)))
            .SelectMany(path => path.Value.EnumerateObject()
                .Where(operation => operation.Name is "get" or "post" or "put" or "delete" or "patch")
                .Select(operation => (Path: path.Name, Method: operation.Name, Contract: operation.Value)))
            .ToArray();

        Assert.Equal(24, operations.Length);
        Assert.Equal(24, operations.Select(operation => operation.Contract.GetProperty("operationId").GetString()).Distinct().Count());
        foreach (var operation in operations)
        {
            Assert.StartsWith("Auxiliary_", operation.Contract.GetProperty("operationId").GetString());
            Assert.False(string.IsNullOrWhiteSpace(operation.Contract.GetProperty("summary").GetString()));
            Assert.Contains("Efeito operacional:", operation.Contract.GetProperty("description").GetString());
        }

        var customerHistory = operations.Single(operation =>
            operation.Path == "/api/v1/customers/{customerId}/history" && operation.Method == "get").Contract;
        Assert.Contains("customers.read", customerHistory.GetProperty("description").GetString());
        Assert.Contains("scheduling.read", customerHistory.GetProperty("description").GetString());
        Assert.Contains("SCHEDULING", customerHistory.GetProperty("description").GetString());

        var fileDownload = operations.Single(operation =>
            operation.Path == "/api/files/{id}" && operation.Method == "get").Contract;
        Assert.Contains("metadado, unidade", fileDownload.GetProperty("description").GetString());
        Assert.True(fileDownload.GetProperty("responses").TryGetProperty("404", out _));

        var health = operations.Single(operation => operation.Path == "/api/health").Contract;
        Assert.True(health.TryGetProperty("security", out var healthSecurity));
        Assert.Empty(healthSecurity.EnumerateArray());
        Assert.Contains("sem expor mensagens internas", health.GetProperty("description").GetString());
        Assert.True(health.GetProperty("responses").TryGetProperty("503", out _));
    }

    private sealed class ApiFactory(string environment) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:ShineDb", "Host=localhost;Port=5432;Database=unused;Username=unused;Password=unused");
            builder.UseSetting("Jwt:Secret", "Shine.OpenApi.Tests.Jwt.Secret.With.More.Than.32.Bytes");
            builder.UseSetting("Jwt:Issuer", "Shine");
            builder.UseSetting("Jwt:Audience", "Shine.Api");
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.UseSetting("Cors:AllowedOrigins:0", "https://openapi-tests.shine.example");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
