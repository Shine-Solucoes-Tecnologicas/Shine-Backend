using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

namespace Shine.Api;

public static class AuthenticationRateLimitPolicies
{
    public const string Login = "auth-login";
    public const string Registration = "auth-registration";
    public const string Refresh = "auth-refresh";
    public const string PasswordRecovery = "auth-password-recovery";
}

public sealed class AuthenticationRateLimitingOptions
{
    public EndpointRateLimitOptions Login { get; init; } = new(10, TimeSpan.FromMinutes(1));
    public EndpointRateLimitOptions Registration { get; init; } = new(5, TimeSpan.FromMinutes(5));
    public EndpointRateLimitOptions Refresh { get; init; } = new(30, TimeSpan.FromMinutes(1));
    public EndpointRateLimitOptions PasswordRecovery { get; init; } = new(5, TimeSpan.FromMinutes(15));

    public bool IsValid() => Login.IsValid() && Registration.IsValid() && Refresh.IsValid() && PasswordRecovery.IsValid();
}

public sealed record EndpointRateLimitOptions(int PermitLimit, TimeSpan Window)
{
    public EndpointRateLimitOptions() : this(1, TimeSpan.FromMinutes(1)) { }
    public bool IsValid() => PermitLimit > 0 && Window > TimeSpan.Zero;
}

public sealed class ApiCorsOptions
{
    public string[] AllowedOrigins { get; init; } = [];
    public bool AllowCredentials { get; init; }
}

public static class SecurityConfiguration
{
    public const string CorsPolicy = "Frontend";

    public static IServiceCollection AddApiSecurityConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<AuthenticationRateLimitingOptions>()
            .Bind(configuration.GetSection("AuthenticationRateLimiting"))
            .Validate(options => options.IsValid(), "Authentication rate limits must have a positive permit limit and window.")
            .ValidateOnStart();

        var rateLimits = configuration.GetSection("AuthenticationRateLimiting").Get<AuthenticationRateLimitingOptions>() ?? new();
        services.AddRateLimiter(options =>
        {
            AddFixedWindowPolicy(options, AuthenticationRateLimitPolicies.Login, rateLimits.Login);
            AddFixedWindowPolicy(options, AuthenticationRateLimitPolicies.Registration, rateLimits.Registration);
            AddFixedWindowPolicy(options, AuthenticationRateLimitPolicies.Refresh, rateLimits.Refresh);
            AddFixedWindowPolicy(options, AuthenticationRateLimitPolicies.PasswordRecovery, rateLimits.PasswordRecovery);
            options.OnRejected = WriteRateLimitResponseAsync;
        });

        services.AddOptions<ApiCorsOptions>()
            .Bind(configuration.GetSection("Cors"))
            .Validate(options => ValidateCors(options, environment),
                "Cors:AllowedOrigins must contain absolute HTTP(S) origins without paths or wildcards; production requires at least one non-loopback origin.")
            .ValidateOnStart();

        var cors = configuration.GetSection("Cors").Get<ApiCorsOptions>() ?? new();
        services.AddCors(options => options.AddPolicy(CorsPolicy, policy =>
        {
            if (cors.AllowedOrigins.Length > 0)
                policy.WithOrigins(cors.AllowedOrigins).AllowAnyHeader().AllowAnyMethod();
            if (cors.AllowCredentials)
                policy.AllowCredentials();
        }));

        return services;
    }

    private static void AddFixedWindowPolicy(RateLimiterOptions options, string policyName, EndpointRateLimitOptions limit) =>
        options.AddPolicy(policyName, context => RateLimitPartition.GetFixedWindowLimiter(
            $"{policyName}:{ClientAddress(context)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limit.PermitLimit,
                Window = limit.Window,
                QueueLimit = 0,
                AutoReplenishment = true
            }));

    private static string ClientAddress(HttpContext context) =>
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

    private static async ValueTask WriteRateLimitResponseAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var delay)
            ? delay
            : TimeSpan.FromSeconds(1);
        var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            errors = new[]
            {
                new { code = "rate_limit_exceeded", message = "Muitas tentativas. Aguarde antes de tentar novamente." }
            }
        }, cancellationToken);
    }

    private static bool ValidateCors(ApiCorsOptions options, IHostEnvironment environment)
    {
        if (!environment.IsDevelopment() && options.AllowedOrigins.Length == 0)
            return false;

        foreach (var origin in options.AllowedOrigins)
        {
            if (origin == "*" || !Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;
            if (uri.Scheme is not ("http" or "https") || uri.PathAndQuery != "/" || !string.IsNullOrEmpty(uri.Fragment))
                return false;
            if (!environment.IsDevelopment() && (IPAddress.TryParse(uri.Host, out var address) && IPAddress.IsLoopback(address) || uri.IsLoopback))
                return false;
        }

        return true;
    }
}
