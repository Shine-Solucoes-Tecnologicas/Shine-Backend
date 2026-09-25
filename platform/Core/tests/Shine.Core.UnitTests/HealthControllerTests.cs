using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Shine.Api.Controllers;

namespace Shine.UnitTests;

public sealed class HealthControllerTests
{
    [Fact]
    public async Task Public_health_response_redacts_dependency_exception_details()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHealthChecks().AddCheck("dependency", () =>
            HealthCheckResult.Unhealthy("internal-secret", new InvalidOperationException("database-password=secret")));
        await using var provider = services.BuildServiceProvider();
        await using var body = new MemoryStream();
        var context = new DefaultHttpContext
        {
            RequestServices = provider
        };
        context.Response.Body = body;
        var controller = new HealthController
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = context }
        };

        await controller.Get(CancellationToken.None);

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, context.Response.StatusCode);
        body.Position = 0;
        var json = await new StreamReader(body, Encoding.UTF8).ReadToEndAsync();
        Assert.Contains("dependency_unavailable", json);
        Assert.DoesNotContain("internal-secret", json);
        Assert.DoesNotContain("database-password", json);
    }
}
