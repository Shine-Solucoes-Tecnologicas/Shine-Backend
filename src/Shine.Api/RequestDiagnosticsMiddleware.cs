using System.Diagnostics;

namespace Shine.Api;

public sealed class RequestDiagnosticsMiddleware(
    RequestDelegate next,
    ILogger<RequestDiagnosticsMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = GetOrCreateCorrelationId(context.Request.Headers[HeaderName].FirstOrDefault());
        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
            {
                await next(context);
            }
        }
        finally
        {
            stopwatch.Stop();
            logger.LogInformation(
                "HTTP request completed {HttpMethod} {RequestPath} with status {StatusCode} in {DurationMs}ms",
                context.Request.Method,
                context.Request.Path,
                context.Response.StatusCode,
                stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static string GetOrCreateCorrelationId(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed.ToString("D") : Guid.NewGuid().ToString("D");
}
