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
        ValidateJwt(configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions());
        ValidateRabbitMq(configuration.GetSection("RabbitMq").Get<RabbitMqOptions>() ?? new RabbitMqOptions());
        if (configuration.GetValue<bool>("PasswordRecovery:MockDelivery"))
            Fail("PasswordRecovery:MockDelivery must be disabled in Production.");
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
