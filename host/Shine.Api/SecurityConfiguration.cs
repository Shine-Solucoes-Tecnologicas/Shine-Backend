using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
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

public sealed class TrustedProxyOptions
{
    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];

    public bool IsValid() =>
        KnownProxies.All(value => IPAddress.TryParse(value, out _)) &&
        KnownNetworks.All(value => System.Net.IPNetwork.TryParse(value, out _));
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

        services.AddOptions<TrustedProxyOptions>()
            .Bind(configuration.GetSection("TrustedProxies"))
            .Validate(options => options.IsValid(),
                "TrustedProxies entries must be valid IP addresses or CIDR networks.")
            .ValidateOnStart();
        var trustedProxies = configuration.GetSection("TrustedProxies").Get<TrustedProxyOptions>() ?? new();
        services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownProxies.Clear();
            options.KnownIPNetworks.Clear();
            if (trustedProxies.KnownProxies.Length == 0 && trustedProxies.KnownNetworks.Length == 0)
            {
                // Empty trust collections mean "trust every proxy" to ForwardedHeadersMiddleware.
                // Unroutable sentinels make the secure default explicitly trust none.
                options.KnownProxies.Add(IPAddress.None);
                options.KnownProxies.Add(IPAddress.IPv6None);
            }
            foreach (var proxy in trustedProxies.KnownProxies)
                options.KnownProxies.Add(IPAddress.Parse(proxy));
            foreach (var network in trustedProxies.KnownNetworks)
                options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
        });

        var rateLimits = configuration.GetSection("AuthenticationRateLimiting").Get<AuthenticationRateLimitingOptions>() ?? new();
        services.AddRateLimiter(options =>
        {
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
                TryGetAuthenticationLimit(context, rateLimits, out var policyName, out var limit)
                    ? FixedWindowPartition($"{policyName}:ip:{ClientAddress(context)}", limit)
                    : RateLimitPartition.GetNoLimiter("non-authentication"));
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
        options.AddPolicy(policyName, context =>
        {
            var identity = AuthenticationRateLimitIdentityMiddleware.GetPartitionIdentity(context);
            var partition = identity == "anonymous"
                ? $"{policyName}:anonymous:{ClientAddress(context)}"
                : $"{policyName}:identity:{identity}";
            return FixedWindowPartition(partition, limit);
        });

    private static RateLimitPartition<string> FixedWindowPartition(string partition, EndpointRateLimitOptions limit) =>
        RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limit.PermitLimit,
            Window = limit.Window,
            QueueLimit = 0,
            AutoReplenishment = true
        });

    private static bool TryGetAuthenticationLimit(
        HttpContext context,
        AuthenticationRateLimitingOptions limits,
        out string policyName,
        out EndpointRateLimitOptions limit)
    {
        policyName = context.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName ?? string.Empty;
        limit = policyName switch
        {
            AuthenticationRateLimitPolicies.Login => limits.Login,
            AuthenticationRateLimitPolicies.Registration => limits.Registration,
            AuthenticationRateLimitPolicies.Refresh => limits.Refresh,
            AuthenticationRateLimitPolicies.PasswordRecovery => limits.PasswordRecovery,
            _ => null!
        };
        return limit is not null;
    }

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
