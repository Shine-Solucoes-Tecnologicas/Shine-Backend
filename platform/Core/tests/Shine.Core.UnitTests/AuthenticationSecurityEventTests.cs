using System.Text.Json;
using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class AuthenticationSecurityEventTests
{
    [Fact]
    public void Builder_uses_allowlists_hmac_and_normalized_client_without_retaining_raw_values()
    {
        const string email = "Sensitive.User@Example.Test";
        const string address = "203.0.113.42";
        const string userAgent = "Mozilla/5.0 (Windows NT 10.0) Chrome/124.0.0.0 SecretMarker";
        var builder = CreateBuilder("v1", "Dedicated.Security.Fingerprint.Secret.Key.Version.One.2026");

        var first = builder.Build(new(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Denied,
            AuthenticationSecurityReasons.InvalidCredentials, "correlation-1", SubjectIdentifier: email,
            NetworkAddress: address, UserAgent: userAgent));
        var repeated = builder.Build(new(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Denied,
            AuthenticationSecurityReasons.InvalidCredentials, "correlation-2", SubjectIdentifier: email.ToLowerInvariant(),
            NetworkAddress: address));

        Assert.Equal(first.SubjectFingerprint, repeated.SubjectFingerprint);
        Assert.Equal(first.NetworkFingerprint, repeated.NetworkFingerprint);
        Assert.Equal("v1", first.FingerprintKeyVersion);
        Assert.Equal("Chrome", first.Client.Family);
        Assert.Equal("124", first.Client.MajorVersion);
        Assert.Equal("Windows", first.Client.OperatingSystem);
        var serialized = JsonSerializer.Serialize(first, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain(email, serialized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(address, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("SecretMarker", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_rotation_changes_output_and_records_the_active_version()
    {
        var first = CreateBuilder("v1", "Dedicated.Security.Fingerprint.Secret.Key.Version.One.2026")
            .Build(Input("person@example.test"));
        var rotated = CreateBuilder("v2", "Dedicated.Security.Fingerprint.Secret.Key.Version.Two.2026")
            .Build(Input("person@example.test"));

        Assert.NotEqual(first.SubjectFingerprint, rotated.SubjectFingerprint);
        Assert.Equal("v1", first.FingerprintKeyVersion);
        Assert.Equal("v2", rotated.FingerprintKeyVersion);
    }

    [Fact]
    public void Builder_rejects_unknown_categories_and_unbounded_fingerprint_sources()
    {
        var builder = CreateBuilder("v1", "Dedicated.Security.Fingerprint.Secret.Key.Version.One.2026");

        Assert.Throws<ArgumentException>(() => builder.Build(Input("person@example.test") with { EventName = "authentication.custom" }));
        Assert.Throws<ArgumentException>(() => builder.Build(Input("person@example.test") with { Reason = "RAW DATABASE ERROR" }));
        Assert.Throws<ArgumentException>(() => builder.Build(Input(new string('x', 513))));
    }

    private static AuthenticationSecurityEventInput Input(string identifier) => new(
        AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Denied,
        AuthenticationSecurityReasons.InvalidCredentials, "correlation", SubjectIdentifier: identifier);

    private static AuthenticationSecurityEventBuilder CreateBuilder(string version, string secret) => new(
        Options.Create(new SecurityFingerprintOptions
        {
            ActiveKeyVersion = version,
            Keys = [new SecurityFingerprintKeyOptions { Version = version, Secret = secret }]
        }));
}
