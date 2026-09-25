using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace Shine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/health")]
public sealed class HealthController : ControllerBase
{
    [HttpGet]
    public async Task Get(CancellationToken cancellationToken)
    {
        var report = await HttpContext.RequestServices
            .GetRequiredService<HealthCheckService>()
            .CheckHealthAsync(cancellationToken);

        Response.StatusCode = report.Status == HealthStatus.Healthy ? StatusCodes.Status200OK : StatusCodes.Status503ServiceUnavailable;
        Response.ContentType = "application/json";
        await JsonSerializer.SerializeAsync(Response.Body, new
        {
            status = report.Status.ToString().ToLowerInvariant(),
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString().ToLowerInvariant(),
                    duration = entry.Value.Duration,
                    error = entry.Value.Exception is null ? null : "dependency_unavailable"
                })
        }, cancellationToken: cancellationToken);
    }
}
