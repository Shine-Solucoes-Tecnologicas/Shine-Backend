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

        var schemaComponents = components.GetProperty("schemas");
        var schemas = schemaComponents.EnumerateObject().Select(item => item.Name).ToArray();
        Assert.Contains(schemas, name => name.EndsWith("CustomerErrorResponse", StringComparison.Ordinal));
        Assert.Contains(schemas, name => name.EndsWith("PagedResponse", StringComparison.Ordinal));
        foreach (var contract in new[] { "LoginResponse", "EmailVerificationConfirmationResponse", "RefreshResponse", "TenantSelectionResponse" })
        {
            var schemaName = schemas.Single(name => name.EndsWith(contract, StringComparison.Ordinal));
            Assert.False(schemaComponents.GetProperty(schemaName).GetProperty("properties")
                .TryGetProperty("refreshToken", out _));
        }

        var registrationRequestName = schemas.Single(name => name.EndsWith("RegistrationRequest", StringComparison.Ordinal));
        var registrationRequestProperties = schemaComponents.GetProperty(registrationRequestName).GetProperty("properties");
        Assert.True(registrationRequestProperties.TryGetProperty("organizationName", out _));
        Assert.True(registrationRequestProperties.TryGetProperty("unitName", out _));
        Assert.False(registrationRequestProperties.TryGetProperty("tenantName", out _));

        var dashboardSave = paths.GetProperty("/api/dashboard/layout").GetProperty("put");
        Assert.Contains("expectedVersion", dashboardSave.GetProperty("description").GetString());
        Assert.True(dashboardSave.GetProperty("responses").TryGetProperty("409", out var dashboardConflict));
        Assert.Contains("StaleDashboardLayoutProblem", dashboardConflict.GetRawText());
        var dashboardRequestName = schemas.Single(name => name.EndsWith("DashboardLayoutRequest", StringComparison.Ordinal));
        Assert.True(schemaComponents.GetProperty(dashboardRequestName).GetProperty("properties")
            .TryGetProperty("expectedVersion", out _));
        var dashboardRead = paths.GetProperty("/api/dashboard/layout").GetProperty("get");
        Assert.Contains("user -> tenant -> system", dashboardRead.GetProperty("description").GetString());
        var dashboardReset = paths.GetProperty("/api/dashboard/layout/reset").GetProperty("post");
        Assert.True(dashboardReset.GetProperty("responses").TryGetProperty("409", out _));
        var dashboardResultName = schemas.Single(name => name.EndsWith("DashboardLayoutResult", StringComparison.Ordinal));
        var dashboardResultProperties = schemaComponents.GetProperty(dashboardResultName).GetProperty("properties");
        Assert.True(dashboardResultProperties.TryGetProperty("source", out _));
        Assert.True(dashboardResultProperties.TryGetProperty("manifestVersion", out _));

        var authenticationErrorName = schemas.Single(name => name.EndsWith("AuthenticationErrorResponse", StringComparison.Ordinal));
        foreach (var operation in new[]
                 {
                     paths.GetProperty("/api/auth/login").GetProperty("post"),
                     paths.GetProperty("/api/auth/register").GetProperty("post"),
                     paths.GetProperty("/api/auth/refresh").GetProperty("post"),
                     paths.GetProperty("/api/auth/select-tenant").GetProperty("post")
                 })
        {
            var errorResponses = operation.GetProperty("responses").EnumerateObject()
                .Where(item => item.Name is "400" or "401" or "403" or "409" or "429")
                .ToArray();
            Assert.NotEmpty(errorResponses);
            Assert.All(errorResponses, item => Assert.EndsWith($"/{authenticationErrorName}",
                item.Value.GetProperty("content").GetProperty("application/json")
                    .GetProperty("schema").GetProperty("$ref").GetString()));
        }
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

        Assert.Equal(37, operations.Length);
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

        var canonicalSelection = operations.Single(operation => operation.Path == "/api/auth/select-tenant").Contract;
        Assert.False(canonicalSelection.TryGetProperty("deprecated", out var canonicalDeprecated) && canonicalDeprecated.GetBoolean());
        foreach (var aliasPath in new[] { "/api/tenants/select", "/api/tenants/switch" })
        {
            var alias = operations.Single(operation => operation.Path == aliasPath).Contract;
            Assert.True(alias.GetProperty("deprecated").GetBoolean());
            Assert.Contains("/api/auth/select-tenant", alias.GetProperty("description").GetString());
        }

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

        Assert.Equal(20, operations.Length);
        Assert.Equal(20, operations.Select(operation => operation.Contract.GetProperty("operationId").GetString()).Distinct().Count());
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

        var capacity = operations.Single(operation =>
            operation.Path == "/api/scheduling/professionals/{professionalId}/capacity" && operation.Method == "get").Contract;
        Assert.Contains("scheduling.read", capacity.GetProperty("description").GetString());
        Assert.Contains("bucket inteiro", capacity.GetProperty("description").GetString());
        var capacitySchema = document.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Single(schema => schema.Name.EndsWith("EffectiveProfessionalCapacityResponse", StringComparison.Ordinal)).Value;
        var capacityProperties = capacitySchema.GetProperty("properties");
        Assert.True(capacityProperties.TryGetProperty("effectiveCapacity", out _));
        Assert.True(capacityProperties.TryGetProperty("defaultCapacity", out _));
        Assert.True(capacityProperties.TryGetProperty("source", out _));
        Assert.True(capacityProperties.TryGetProperty("gridIntervalMinutes", out _));
        Assert.True(capacityProperties.TryGetProperty("partialOccupancyConsumesWholeBucket", out _));

        var slotSchemas = document.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Where(schema => schema.Name.EndsWith("AvailabilitySlotResponse", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, slotSchemas.Length);
        Assert.All(slotSchemas, schema =>
        {
            var properties = schema.Value.GetProperty("properties");
            Assert.True(properties.TryGetProperty("startsAtUtc", out _));
            Assert.True(properties.TryGetProperty("endsAtUtc", out _));
            Assert.True(properties.TryGetProperty("utcOffset", out _));
            Assert.True(properties.TryGetProperty("isAmbiguousLocalTime", out _));
        });

        var create = operations.Single(operation =>
            operation.Path == "/api/scheduling/appointments" && operation.Method == "post").Contract;
        Assert.Contains("entitlement", create.GetProperty("description").GetString());
        var createConflict = create.GetProperty("responses").GetProperty("409")
            .GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
        var conflictDiscriminator = createConflict.GetProperty("discriminator");
        Assert.Equal("code", conflictDiscriminator.GetProperty("propertyName").GetString());
        Assert.Equal(5, conflictDiscriminator.GetProperty("mapping").EnumerateObject().Count());
        Assert.Equal(5, createConflict.GetProperty("oneOf").GetArrayLength());

        var reschedule = operations.Single(operation =>
            operation.Path == "/api/scheduling/appointments/{appointmentId}/reschedule" && operation.Method == "put").Contract;
        Assert.Contains("ExpectedVersion", reschedule.GetProperty("description").GetString());
        var stale = reschedule.GetProperty("responses").GetProperty("412")
            .GetProperty("content").GetProperty("application/problem+json").GetProperty("schema");
        Assert.Equal("code", stale.GetProperty("discriminator").GetProperty("propertyName").GetString());
        Assert.Single(stale.GetProperty("oneOf").EnumerateArray());

        var problemSchemas = document.RootElement.GetProperty("components").GetProperty("schemas")
            .EnumerateObject().Where(schema => schema.Name.Contains("SchedulingProblemContract", StringComparison.Ordinal)).ToArray();
        Assert.Equal(6, problemSchemas.Length);
        Assert.All(problemSchemas, schema =>
        {
            var properties = schema.Value.GetProperty("properties");
            Assert.True(properties.TryGetProperty("type", out _));
            Assert.True(properties.TryGetProperty("title", out _));
            Assert.True(properties.TryGetProperty("status", out _));
            Assert.True(properties.TryGetProperty("code", out _));
            Assert.True(properties.TryGetProperty("detail", out _));
            Assert.True(properties.TryGetProperty("details", out _));
        });

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

        Assert.Equal(25, operations.Length);
        Assert.Equal(25, operations.Select(operation => operation.Contract.GetProperty("operationId").GetString()).Distinct().Count());
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
            builder.UseSetting("WebSession:PublicApiOrigin", "https://api.shine.example");
            builder.UseSetting("WebSession:SiteDomain", "shine.example");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services => services.RemoveAll<IHostedService>());
        }
    }
}
