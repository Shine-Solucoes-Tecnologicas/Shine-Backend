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
