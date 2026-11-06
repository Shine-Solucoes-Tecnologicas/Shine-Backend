using Npgsql;
using Shine.Infrastructure;

namespace Shine.Api;

public static class ProductionConfigurationValidation
{
    private static readonly HashSet<string> WeakValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "shine", "guest", "postgres", "password", "secret", "admin", "changeme", "change-me"
    };

    public static void ValidateProductionConfiguration(this IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction()) return;

        ValidateDatabase(configuration.GetConnectionString("ShineDb"));
        var jwt = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
        ValidateJwt(jwt);
        ValidateSecurityFingerprints(configuration.GetSection("SecurityEvents:Fingerprinting").Get<SecurityFingerprintOptions>() ?? new SecurityFingerprintOptions(), jwt);
        ValidateRabbitMq(configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions());
        if (!configuration.GetValue<bool>("Cors:AllowCredentials"))
            Fail("Cors:AllowCredentials must be enabled in Production for the web session cookie.");
        if (configuration.GetValue<bool>("WebSession:AllowLegacyBodyTransport"))
            Fail("WebSession:AllowLegacyBodyTransport must be disabled in Production.");
        if (configuration.GetValue<bool>("PasswordRecovery:MockDelivery"))
            Fail("PasswordRecovery:MockDelivery must be disabled in Production.");
        if (configuration.GetValue<bool>("EmailVerification:MockDelivery"))
            Fail("EmailVerification:MockDelivery must be disabled in Production.");
    }

    private static void ValidateDatabase(string? connectionString)
    {
        try
        {
            var builder = new NpgsqlConnectionStringBuilder(connectionString);
            if (string.IsNullOrWhiteSpace(builder.Host) || string.IsNullOrWhiteSpace(builder.Username) || IsWeak(builder.Password))
                Fail("ConnectionStrings:ShineDb must use non-placeholder production credentials.");
        }
        catch (ArgumentException)
        {
            Fail("ConnectionStrings:ShineDb must be a valid PostgreSQL connection string.");
        }
    }

    private static void ValidateJwt(JwtOptions options)
    {
        if (options.SigningKeys.Count == 0 || options.SigningKeys.All(key => string.IsNullOrWhiteSpace(key.Id) && string.IsNullOrWhiteSpace(key.Secret)))
            Fail("Jwt:SigningKeys and Jwt:ActiveKeyId are required in Production; Jwt:Secret is development-only.");
        if (!JwtSigningKeyRing.TryValidate(options, out _))
            Fail("Jwt signing key configuration is invalid for Production.");
        if (options.SigningKeys.Any(key => IsWeak(key.Secret)))
            Fail("Jwt:SigningKeys contains a placeholder or known development secret.");
    }

    private static void ValidateRabbitMq(RabbitMqOptions options)
    {
        if (!options.Enabled) return;
        if (string.IsNullOrWhiteSpace(options.HostName) || string.IsNullOrWhiteSpace(options.UserName) || IsWeak(options.Password) ||
            string.Equals(options.UserName, "guest", StringComparison.OrdinalIgnoreCase))
            Fail("RabbitMq production credentials must be explicitly configured and non-placeholder.");
    }

    private static void ValidateSecurityFingerprints(SecurityFingerprintOptions options, JwtOptions jwt)
    {
        if (!SecurityFingerprintOptions.TryValidate(options, out _))
            Fail("SecurityEvents:Fingerprinting requires a valid active HMAC key ring in Production.");
        if (options.Keys.Any(key => IsWeak(key.Secret)))
            Fail("SecurityEvents:Fingerprinting contains a placeholder or development key.");
        var jwtSecrets = jwt.SigningKeys.Select(key => key.Secret).Append(jwt.Secret)
            .Where(secret => !string.IsNullOrWhiteSpace(secret)).ToHashSet(StringComparer.Ordinal);
        if (options.Keys.Any(key => jwtSecrets.Contains(key.Secret)))
            Fail("SecurityEvents:Fingerprinting must use keys dedicated to HMAC fingerprints, separate from JWT signing keys.");
    }

    private static bool IsWeak(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || WeakValues.Contains(value.Trim())) return true;
        var normalized = value.Trim().ToLowerInvariant();
        return normalized.Contains("replace-with", StringComparison.Ordinal) ||
               normalized.Contains("change.me", StringComparison.Ordinal) ||
               normalized.Contains("example", StringComparison.Ordinal) ||
               normalized.Contains("local.development", StringComparison.Ordinal) ||
               normalized.Contains("must.be.at.least", StringComparison.Ordinal) ||
               normalized.Contains('<') || normalized.Contains('>');
    }

    private static void Fail(string message) => throw new InvalidOperationException(message);
}
