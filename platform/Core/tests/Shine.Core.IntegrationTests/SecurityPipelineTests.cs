using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Shine.Api;
using Shine.Api.Controllers;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class SecurityPipelineTests(DatabaseFixture fixture)
{
    private const string JwtSecret = "integration-tests-only-secret-with-at-least-32-bytes";
    private const string ProductionConnectionString = "Host=database.internal;Port=5432;Database=shine;Username=shine_app;Password=production-test-password-with-32-bytes";
    private const string ProductionJwtSecret = "production-test-jwt-secret-with-at-least-32-bytes";

    [Fact]
    public async Task Login_rate_limit_has_stable_contract_ignores_untrusted_forwarding_and_recovers()
    {
        await using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["AuthenticationRateLimiting:Login:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Login:Window"] = "00:00:01"
        });
        using var client = factory.CreateClient();

        using var first = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("", ""))
        };
        first.Headers.Add("X-Forwarded-For", "198.51.100.10");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(first)).StatusCode);

        using var second = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new LoginRequest("", ""))
        };
        second.Headers.Add("X-Forwarded-For", "203.0.113.20");
        var rejected = await client.SendAsync(second);

        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.True(rejected.Headers.TryGetValues("Retry-After", out var retryValues));
        Assert.True(int.Parse(Assert.Single(retryValues)) >= 1);
        using var payload = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
        Assert.Equal("rate_limit_exceeded", payload.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());

        await Task.Delay(TimeSpan.FromMilliseconds(1_100));
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest("", ""))).StatusCode);
    }

    [Fact]
    public async Task Login_rate_limit_blocks_a_normalized_identifier_across_different_client_addresses_without_exposing_it()
    {
        var proxyAddress = IPAddress.Parse("10.0.0.10");
        await using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["AuthenticationRateLimiting:Login:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Login:Window"] = "00:01:00",
            ["TrustedProxies:KnownProxies:0"] = proxyAddress.ToString()
        }, remoteIpAddress: proxyAddress);
        using var client = factory.CreateClient();
        var firstEmail = $"partition-{Guid.NewGuid():N}@example.test";
        var secondEmail = $"partition-{Guid.NewGuid():N}@example.test";

        async Task<HttpResponseMessage> LoginFrom(string address, object body)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(body)
            };
            request.Headers.Add("X-Forwarded-For", address);
            return await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFrom("198.51.100.10",
            new LoginRequest(firstEmail, "wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFrom("203.0.113.20",
            new LoginRequest(secondEmail, "wrong"))).StatusCode);

        var normalizedDuplicate = await LoginFrom("192.0.2.30",
            new { Email = $"  {firstEmail.ToUpperInvariant()}  ", Password = "wrong" });
        Assert.Equal(HttpStatusCode.TooManyRequests, normalizedDuplicate.StatusCode);
        Assert.DoesNotContain(firstEmail, await normalizedDuplicate.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_rate_limit_blocks_one_client_address_across_different_identifiers()
    {
        await using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["AuthenticationRateLimiting:Login:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Login:Window"] = "00:01:00"
        });
        using var client = factory.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest($"first-{Guid.NewGuid():N}@example.test", "wrong"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/login",
                new LoginRequest($"second-{Guid.NewGuid():N}@example.test", "wrong"))).StatusCode);
    }

    [Fact]
    public async Task Login_rate_limit_extracts_the_identifier_from_chunked_json()
    {
        await using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["AuthenticationRateLimiting:Login:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Login:Window"] = "00:01:00"
        });
        using var client = factory.CreateClient();
        var email = $"chunked-{Guid.NewGuid():N}@example.test";
        var json = JsonSerializer.Serialize(new LoginRequest(email, "wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsync("/api/auth/login", new ChunkedJsonContent(json))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, "wrong"))).StatusCode);
    }

    [Fact]
    public void Trusted_proxy_configuration_replaces_framework_defaults_with_explicit_addresses()
    {
        using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["TrustedProxies:KnownProxies:0"] = "10.0.0.10",
            ["TrustedProxies:KnownNetworks:0"] = "192.0.2.0/24"
        });
        using var scope = factory.Services.CreateScope();

        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Builder.ForwardedHeadersOptions>>().Value;

        Assert.Equal(ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto, options.ForwardedHeaders);
        Assert.Equal(1, options.ForwardLimit);
        Assert.Equal(IPAddress.Parse("10.0.0.10"), Assert.Single(options.KnownProxies));
        Assert.Equal(System.Net.IPNetwork.Parse("192.0.2.0/24"), Assert.Single(options.KnownIPNetworks));
    }

    [Fact]
    public async Task Trusted_proxy_forwarding_changes_the_effective_client_partition()
    {
        var proxyAddress = IPAddress.Parse("10.0.0.10");
        await using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["AuthenticationRateLimiting:Login:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Login:Window"] = "00:01:00",
            ["TrustedProxies:KnownProxies:0"] = proxyAddress.ToString()
        }, remoteIpAddress: proxyAddress);
        using var client = factory.CreateClient();
        async Task<HttpResponseMessage> LoginFrom(string address, string email)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
            {
                Content = JsonContent.Create(new LoginRequest(email, "wrong"))
            };
            request.Headers.Add("X-Forwarded-For", address);
            return await client.SendAsync(request);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFrom("198.51.100.10",
            $"proxy-a-{Guid.NewGuid():N}@example.test")).StatusCode);
        var secondEmail = $"proxy-b-{Guid.NewGuid():N}@example.test";
        Assert.Equal(HttpStatusCode.Unauthorized, (await LoginFrom("203.0.113.20", secondEmail)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await LoginFrom("203.0.113.20",
            $"proxy-c-{Guid.NewGuid():N}@example.test")).StatusCode);
    }

    [Fact]
    public async Task Every_public_authentication_endpoint_is_limited_independently()
    {
        await using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["AuthenticationRateLimiting:Login:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Login:Window"] = "00:01:00",
            ["AuthenticationRateLimiting:Registration:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Registration:Window"] = "00:01:00",
            ["AuthenticationRateLimiting:Refresh:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:Refresh:Window"] = "00:01:00",
            ["AuthenticationRateLimiting:PasswordRecovery:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:PasswordRecovery:Window"] = "00:01:00",
            ["AuthenticationRateLimiting:EmailVerificationResend:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:EmailVerificationResend:Window"] = "00:01:00",
            ["AuthenticationRateLimiting:EmailVerificationConfirm:PermitLimit"] = "1",
            ["AuthenticationRateLimiting:EmailVerificationConfirm:Window"] = "00:01:00"
        });
        using var client = factory.CreateClient();

        var requests = new (string Path, object Body, HttpStatusCode FirstStatus)[]
        {
            ("/api/auth/login", new LoginRequest("", ""), HttpStatusCode.Unauthorized),
            ("/api/auth/register", new RegistrationRequest("", "", ""), HttpStatusCode.BadRequest),
            ("/api/auth/refresh", new RefreshRequest("invalid"), HttpStatusCode.Unauthorized),
            ("/api/auth/password/recovery", new PasswordRecoveryRequest(""), HttpStatusCode.Accepted),
            ("/api/auth/email-verification/resend", new EmailVerificationResendRequest(""), HttpStatusCode.Accepted),
            ("/api/auth/email-verification/confirm", new EmailVerificationConfirmRequest("invalid"), HttpStatusCode.BadRequest)
        };

        foreach (var request in requests)
        {
            Assert.Equal(request.FirstStatus, (await client.PostAsJsonAsync(request.Path, request.Body)).StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsJsonAsync(request.Path, request.Body)).StatusCode);
        }
    }

    [Fact]
    public async Task Unverified_user_cannot_login_even_with_the_correct_password()
    {
        const string password = "Strong-password-123!";
        var user = new User(
            $"pending-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password),
            emailVerified: false);
        await using (var db = fixture.CreateDb())
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString());
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var verificationDb = fixture.CreateDb();
        Assert.False(await verificationDb.RefreshTokens.AnyAsync(token => token.UserId == user.Id));
    }

    [Fact]
    public async Task Login_executes_password_verification_for_unknown_inactive_and_locked_users()
    {
        var inactive = new User($"inactive-{Guid.NewGuid():N}@example.test", "inactive-hash");
        inactive.Block("security test", Guid.NewGuid(), DateTime.UtcNow);
        var locked = new User($"locked-{Guid.NewGuid():N}@example.test", "locked-hash");
        locked.RegisterFailedLogin(DateTime.UtcNow, 1, TimeSpan.FromMinutes(5));
        await using (var db = fixture.CreateDb())
        {
            db.Users.AddRange(inactive, locked);
            await db.SaveChangesAsync();
        }

        var verifier = new RecordingPasswordHashService();
        await using var factory = new ApiFactory(ConnectionString(), passwordHashService: verifier);
        using var client = factory.CreateClient();

        var unknown = $"unknown-{Guid.NewGuid():N}@example.test";
        var unknownResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(unknown, "wrong"));
        var inactiveResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(inactive.Email, "wrong"));
        var lockedResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(locked.Email, "wrong"));
        Assert.All(new[] { unknownResponse, inactiveResponse, lockedResponse }, response =>
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        var publicBodies = await Task.WhenAll(new[] { unknownResponse, inactiveResponse, lockedResponse }
            .Select(async response => JsonDocument.Parse(await response.Content.ReadAsStringAsync())));
        Assert.All(publicBodies, body =>
        {
            Assert.Equal(401, body.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("Unauthorized", body.RootElement.GetProperty("title").GetString());
        });

        Assert.Equal(3, verifier.EncodedHashes.Count);
        Assert.Null(verifier.EncodedHashes[0]);
        Assert.Equal("inactive-hash", verifier.EncodedHashes[1]);
        Assert.Equal("locked-hash", verifier.EncodedHashes[2]);
    }

    [Fact]
    public async Task Cors_preflight_allows_configured_origin_and_omits_header_for_blocked_origin()
    {
        await using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://frontend.example.test"
        });
        using var client = factory.CreateClient();

        var allowed = await Preflight(client, "https://frontend.example.test");
        Assert.Equal(HttpStatusCode.NoContent, allowed.StatusCode);
        Assert.Equal("https://frontend.example.test", allowed.Headers.GetValues("Access-Control-Allow-Origin").Single());

        var blocked = await Preflight(client, "https://attacker.example.test");
        Assert.Equal(HttpStatusCode.NoContent, blocked.StatusCode);
        Assert.False(blocked.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Login_and_refresh_issue_tokens_accepted_by_the_official_bearer_pipeline()
    {
        const string password = "Strong-password-123!";
        var user = new User($"jwt-flow-{Guid.NewGuid():N}@example.test", new Pbkdf2PasswordHashService().Hash(password));
        var tenant = new Tenant($"JWT flow {Guid.NewGuid():N}");
        var membership = new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true);
        await using (var db = fixture.CreateDb())
        {
            db.AddRange(user, tenant, membership);
            await db.SaveChangesAsync();
        }

        await using var factory = new ApiFactory(ConnectionString());
        using var client = factory.CreateClient();
        var loginHttpResponse = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        Assert.Equal(HttpStatusCode.OK, loginHttpResponse.StatusCode);
        var login = Assert.IsType<LoginResponse>(await loginHttpResponse.Content.ReadFromJsonAsync<LoginResponse>());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/sessions")).StatusCode);

        var refreshHttpResponse = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(login.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshHttpResponse.StatusCode);
        var refresh = Assert.IsType<RefreshResponse>(await refreshHttpResponse.Content.ReadFromJsonAsync<RefreshResponse>());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refresh.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/sessions")).StatusCode);
    }

    [Fact]
    public void Production_fails_startup_without_an_explicit_cors_origin()
    {
        using var factory = new ApiFactory(ConnectionString(), environment: Environments.Production);
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Cors:AllowedOrigins", error.ToString());
    }

    [Fact]
    public void Production_rejects_loopback_cors_origins()
    {
        using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
        }, environment: Environments.Production);
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Cors:AllowedOrigins", error.ToString());
    }

    [Theory]
    [InlineData("ConnectionStrings:ShineDb", "Host=database.internal;Database=shine;Username=shine;Password=shine", "ConnectionStrings:ShineDb")]
    [InlineData("Jwt:SigningKeys:0:Secret", "replace-with-a-long-random-secret-at-least-32-characters", "Jwt:SigningKeys")]
    [InlineData("RabbitMq:Password", "guest", "RabbitMq")]
    public void Production_rejects_known_development_or_placeholder_credentials(string key, string value, string expectedField)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://frontend.example.test",
            [key] = value
        };
        if (key.StartsWith("RabbitMq", StringComparison.Ordinal))
        {
            settings["RabbitMq:Enabled"] = "true";
            settings["RabbitMq:HostName"] = "rabbitmq.internal";
            settings["RabbitMq:UserName"] = "shine_app";
        }

        using var factory = new ApiFactory(ConnectionString(), settings, environment: Environments.Production);
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains(expectedField, error.ToString());
        Assert.DoesNotContain(value, error.ToString());
    }

    [Fact]
    public void Production_rejects_legacy_jwt_secret_even_when_it_is_long()
    {
        using var factory = new ApiFactory(ConnectionString(), new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "https://frontend.example.test",
            ["Jwt:SigningKeys:0:Id"] = null,
            ["Jwt:SigningKeys:0:Secret"] = null,
            ["Jwt:ActiveKeyId"] = null,
            ["Jwt:Secret"] = ProductionJwtSecret
        }, environment: Environments.Production);
        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        Assert.Contains("Jwt:SigningKeys", error.ToString());
        Assert.DoesNotContain(ProductionJwtSecret, error.ToString());
    }

    private static async Task<HttpResponseMessage> Preflight(HttpClient client, string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        return await client.SendAsync(request);
    }

    private string ConnectionString() => fixture.ConnectionString;

    private sealed class RecordingPasswordHashService : IPasswordHashService
    {
        public List<string?> EncodedHashes { get; } = [];
        public string Hash(string password) => $"hash:{password}";
        public bool Verify(string password, string? encodedHash)
        {
            EncodedHashes.Add(encodedHash);
            return false;
        }
    }

    private sealed class ChunkedJsonContent : HttpContent
    {
        private readonly byte[] bytes;

        public ChunkedJsonContent(string json)
        {
            bytes = Encoding.UTF8.GetBytes(json);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override void SerializeToStream(Stream stream, TransportContext? context,
            CancellationToken cancellationToken) => stream.Write(bytes);
    }

    private sealed class ApiFactory(
        string connectionString,
        IReadOnlyDictionary<string, string?>? settings = null,
        IPasswordHashService? passwordHashService = null,
        string environment = "Development",
        IPAddress? remoteIpAddress = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.UseSetting("ConnectionStrings:ShineDb", environment == Environments.Production ? ProductionConnectionString : connectionString);
            if (environment == Environments.Production)
            {
                builder.UseSetting("Jwt:ActiveKeyId", "production-test");
                builder.UseSetting("Jwt:SigningKeys:0:Id", "production-test");
                builder.UseSetting("Jwt:SigningKeys:0:Secret", ProductionJwtSecret);
            }
            else
            {
                builder.UseSetting("Jwt:Secret", JwtSecret);
            }
            builder.UseSetting("Jwt:Issuer", "Shine");
            builder.UseSetting("Jwt:Audience", "Shine.Api");
            builder.UseSetting("RabbitMq:Enabled", "false");
            if (settings is not null)
                foreach (var setting in settings)
                    builder.UseSetting(setting.Key, setting.Value);
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                if (remoteIpAddress is not null)
                    services.AddSingleton<IStartupFilter>(new RemoteIpStartupFilter(remoteIpAddress));
                if (passwordHashService is not null)
                {
                    services.RemoveAll<IPasswordHashService>();
                    services.AddSingleton(passwordHashService);
                }
            });
        }
    }

    private sealed class RemoteIpStartupFilter(IPAddress address) : IStartupFilter
    {
        public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(
            Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => application =>
        {
            application.Use(continuation => async context =>
            {
                context.Connection.RemoteIpAddress = address;
                await continuation(context);
            });
            next(application);
        };
    }
}
