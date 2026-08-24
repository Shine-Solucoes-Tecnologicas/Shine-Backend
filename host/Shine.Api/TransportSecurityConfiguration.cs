using Microsoft.AspNetCore.Http.Extensions;

namespace Shine.Api;

public static class TransportSecurityConfiguration
{
    private const string HealthPath = "/health";

    public static IApplicationBuilder UseTransportSecurity(
        this IApplicationBuilder application,
        IHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            application.UseHsts();
            application.Use(RequireHttpsOutsideHealthCheck);
        }

        application.Use(AddApiSecurityHeaders);
        return application;
    }

    private static async Task RequireHttpsOutsideHealthCheck(HttpContext context, RequestDelegate next)
    {
        if (context.Request.IsHttps || context.Request.Path.StartsWithSegments(HealthPath))
        {
            await next(context);
            return;
        }

        var request = context.Request;
        var host = request.Host.Port is null or 80
            ? new HostString(request.Host.Host)
            : request.Host;
        context.Response.StatusCode = StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = UriHelper.BuildAbsolute(
            Uri.UriSchemeHttps,
            host,
            request.PathBase,
            request.Path,
            request.QueryString);
    }

    private static async Task AddApiSecurityHeaders(HttpContext context, RequestDelegate next)
    {
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;
            headers.TryAdd("X-Content-Type-Options", "nosniff");
            headers.TryAdd("X-Frame-Options", "DENY");
            headers.TryAdd("Referrer-Policy", "no-referrer");
            return Task.CompletedTask;
        });

        await next(context);
    }
}
