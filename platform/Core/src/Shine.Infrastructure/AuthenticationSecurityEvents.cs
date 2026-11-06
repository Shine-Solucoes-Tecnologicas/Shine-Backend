using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public static class AuthenticationSecurityEventNames
{
    public const string Login = "authentication.login";
    public const string LockoutStarted = "authentication.lockout.started";
    public const string LockoutEnded = "authentication.lockout.ended";
    public const string SessionRefresh = "authentication.session.refresh";
    public const string SessionReuseDetected = "authentication.session.reuse_detected";
    public const string SessionRevoked = "authentication.session.revoked";
    public const string PasswordChanged = "authentication.password.changed";
    public const string PasswordResetRequested = "authentication.password.reset_requested";
    public const string PasswordResetCompleted = "authentication.password.reset_completed";
    public const string TenantSelection = "authentication.tenant.selection";
    public const string SuspiciousActivityDetected = "authentication.suspicious_activity.detected";

    internal static readonly IReadOnlySet<string> All = new HashSet<string>(
        [Login, LockoutStarted, LockoutEnded, SessionRefresh, SessionReuseDetected, SessionRevoked,
            PasswordChanged, PasswordResetRequested, PasswordResetCompleted, TenantSelection, SuspiciousActivityDetected],
        StringComparer.Ordinal);
}

public static class AuthenticationSecurityOutcomes
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";
    public const string Detected = "detected";
    internal static readonly IReadOnlySet<string> All = new HashSet<string>([Succeeded, Denied, Detected], StringComparer.Ordinal);
}

public static class AuthenticationSecurityReasons
{
    public const string InvalidCredentials = "INVALID_CREDENTIALS";
    public const string AccountInactive = "ACCOUNT_INACTIVE";
    public const string AccountLocked = "ACCOUNT_LOCKED";
    public const string RateLimited = "RATE_LIMITED";
    public const string TokenExpired = "TOKEN_EXPIRED";
    public const string TokenRevoked = "TOKEN_REVOKED";
    public const string TokenReuseDetected = "TOKEN_REUSE_DETECTED";
    public const string SessionRevoked = "SESSION_REVOKED";
    public const string TenantNotAllowed = "TENANT_NOT_ALLOWED";
    public const string PasswordChanged = "PASSWORD_CHANGED";
    public const string PasswordReset = "PASSWORD_RESET";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string Unknown = "UNKNOWN";

    internal static readonly IReadOnlySet<string> All = new HashSet<string>(
        [InvalidCredentials, AccountInactive, AccountLocked, RateLimited, TokenExpired, TokenRevoked,
            TokenReuseDetected, SessionRevoked, TenantNotAllowed, PasswordChanged, PasswordReset, ValidationFailed, Unknown],
        StringComparer.Ordinal);
}

public sealed class SecurityFingerprintOptions
{
    public string ActiveKeyVersion { get; set; } = string.Empty;
    public List<SecurityFingerprintKeyOptions> Keys { get; set; } = [];

    public static bool TryValidate(SecurityFingerprintOptions options, out string error)
    {
        if (string.IsNullOrWhiteSpace(options.ActiveKeyVersion) || options.ActiveKeyVersion.Length > 40)
        {
            error = "An active fingerprint key version is required.";
            return false;
        }
        if (options.Keys.Count == 0 || options.Keys.Any(key => string.IsNullOrWhiteSpace(key.Version) ||
                key.Version.Length > 40 || string.IsNullOrWhiteSpace(key.Secret) || key.Secret.Length < 32) ||
            options.Keys.Select(key => key.Version).Distinct(StringComparer.Ordinal).Count() != options.Keys.Count ||
            options.Keys.All(key => key.Version != options.ActiveKeyVersion))
        {
            error = "Fingerprint keys must be unique, at least 32 characters long, and include the active version.";
            return false;
        }
        error = string.Empty;
        return true;
    }
}

public sealed class SecurityFingerprintKeyOptions
{
    public string Version { get; set; } = string.Empty;
    public string Secret { get; set; } = string.Empty;
}

public sealed record AuthenticationSecurityEventInput(
    string EventName,
    string Outcome,
    string Reason,
    string CorrelationId,
    Guid? ActorUserId = null,
    Guid? SubjectUserId = null,
    Guid? SessionId = null,
    Guid? TenantId = null,
    string? SubjectIdentifier = null,
    string? NetworkAddress = null,
    string? UserAgent = null,
    DateTime? OccurredAtUtc = null);

public sealed record AuthenticationClientMetadata(string Family, string? MajorVersion, string OperatingSystem, string DeviceClass);

public sealed record AuthenticationSecurityEventEnvelope(
    int SchemaVersion,
    string EventName,
    DateTime OccurredAtUtc,
    string Outcome,
    string Reason,
    string CorrelationId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? ActorUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SubjectUserId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? SessionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? TenantId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SubjectFingerprint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NetworkFingerprint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FingerprintKeyVersion,
    AuthenticationClientMetadata Client);

public interface IAuthenticationSecurityEventBuilder
{
    AuthenticationSecurityEventEnvelope Build(AuthenticationSecurityEventInput input);
}

public sealed class AuthenticationSecurityEventBuilder(IOptions<SecurityFingerprintOptions> options) : IAuthenticationSecurityEventBuilder
{
    private readonly SecurityFingerprintOptions configuration = Validate(options.Value);

    public AuthenticationSecurityEventEnvelope Build(AuthenticationSecurityEventInput input)
    {
        if (!AuthenticationSecurityEventNames.All.Contains(input.EventName)) throw new ArgumentException("Security event name is not allowed.", nameof(input));
        if (!AuthenticationSecurityOutcomes.All.Contains(input.Outcome)) throw new ArgumentException("Security event outcome is not allowed.", nameof(input));
        if (!AuthenticationSecurityReasons.All.Contains(input.Reason)) throw new ArgumentException("Security event reason is not allowed.", nameof(input));
        var correlationId = RequiredBounded(input.CorrelationId, 100, nameof(input.CorrelationId));
        var subject = Fingerprint(input.SubjectIdentifier, 512);
        var network = Fingerprint(input.NetworkAddress, 128);
        return new AuthenticationSecurityEventEnvelope(
            1, input.EventName, NormalizeUtc(input.OccurredAtUtc ?? DateTime.UtcNow), input.Outcome, input.Reason,
            correlationId, input.ActorUserId, input.SubjectUserId, input.SessionId, input.TenantId,
            subject, network, subject is null && network is null ? null : configuration.ActiveKeyVersion,
            NormalizeClient(input.UserAgent));
    }

    private string? Fingerprint(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length > maximumLength) throw new ArgumentException("Fingerprint source exceeds the allowed length.");
        var key = configuration.Keys.Single(item => item.Version == configuration.ActiveKeyVersion);
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key.Secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant();
    }

    private static AuthenticationClientMetadata NormalizeClient(string? userAgent)
    {
        var value = (userAgent ?? string.Empty).Trim();
        if (value.Length > 1024) value = value[..1024];
        var family = value.Contains("Edg/", StringComparison.OrdinalIgnoreCase) ? "Edge" :
            value.Contains("Chrome/", StringComparison.OrdinalIgnoreCase) ? "Chrome" :
            value.Contains("Firefox/", StringComparison.OrdinalIgnoreCase) ? "Firefox" :
            value.Contains("Safari/", StringComparison.OrdinalIgnoreCase) ? "Safari" :
            value.Contains("curl/", StringComparison.OrdinalIgnoreCase) ? "Curl" : "Other";
        var token = family switch { "Edge" => "Edg", "Curl" => "curl", _ => family };
        var match = Regex.Match(value, $@"(?:^|\s|/){Regex.Escape(token)}/(?<major>\d{{1,4}})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var major = match.Success ? match.Groups["major"].Value : null;
        var os = value.Contains("Windows", StringComparison.OrdinalIgnoreCase) ? "Windows" :
            value.Contains("Android", StringComparison.OrdinalIgnoreCase) ? "Android" :
            value.Contains("iPhone", StringComparison.OrdinalIgnoreCase) || value.Contains("iPad", StringComparison.OrdinalIgnoreCase) ? "iOS" :
            value.Contains("Mac OS", StringComparison.OrdinalIgnoreCase) ? "macOS" :
            value.Contains("Linux", StringComparison.OrdinalIgnoreCase) ? "Linux" : "Other";
        var device = value.Contains("Mobile", StringComparison.OrdinalIgnoreCase) || os is "Android" or "iOS" ? "mobile" :
            family is "Curl" ? "automation" : value.Length == 0 ? "unknown" : "desktop";
        return new(family, major, os, device);
    }

    private static SecurityFingerprintOptions Validate(SecurityFingerprintOptions options) =>
        SecurityFingerprintOptions.TryValidate(options, out var error) ? options : throw new OptionsValidationException(
            nameof(SecurityFingerprintOptions), typeof(SecurityFingerprintOptions), [error]);

    private static string RequiredBounded(string value, int maximumLength, string name)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > maximumLength) throw new ArgumentException("Value is required and must be bounded.", name);
        return normalized;
    }

    private static DateTime NormalizeUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}

public interface IAuthenticationSecurityEventEmitter
{
    AuthenticationSecurityEventEnvelope Enqueue(AuthenticationSecurityEventInput input);
}

public sealed class AuthenticationSecurityEventEmitter(
    ShineDbContext db,
    IAuthenticationSecurityEventBuilder builder) : IAuthenticationSecurityEventEmitter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AuthenticationSecurityEventEnvelope Enqueue(AuthenticationSecurityEventInput input)
    {
        var envelope = builder.Build(input);
        var payload = JsonSerializer.Serialize(envelope, JsonOptions);
        db.OperationalLogs.Add(OperationalLog.Create(
            envelope.Outcome == AuthenticationSecurityOutcomes.Succeeded ? "Information" : "Warning",
            "Security.Authentication", payload, correlationId: envelope.CorrelationId));
        if (ShouldAudit(envelope))
        {
            var entityId = envelope.SessionId?.ToString() ?? envelope.SubjectUserId?.ToString() ??
                envelope.ActorUserId?.ToString() ?? envelope.SubjectFingerprint ?? envelope.CorrelationId;
            db.AuditEntries.Add(AuditEntry.Create("AuthenticationSecurityEvent", entityId, "SECURITY_EVENT",
                envelope.ActorUserId ?? envelope.SubjectUserId, envelope.TenantId, envelope.OccurredAtUtc,
                envelope.CorrelationId, newValuesJson: payload));
        }
        return envelope;
    }

    internal static bool ShouldAudit(AuthenticationSecurityEventEnvelope envelope) => envelope.EventName switch
    {
        AuthenticationSecurityEventNames.Login => envelope.Outcome == AuthenticationSecurityOutcomes.Succeeded,
        AuthenticationSecurityEventNames.SessionRefresh => false,
        AuthenticationSecurityEventNames.PasswordResetRequested => false,
        AuthenticationSecurityEventNames.TenantSelection => envelope.Outcome == AuthenticationSecurityOutcomes.Succeeded || envelope.ActorUserId is not null,
        _ => true
    };
}
