using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Shine.Api;
using Shine.Api.Controllers;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class AuthenticationSecurityHttpTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Browser_session_uses_secure_http_only_cookie_csrf_rotation_and_logout_without_exposing_refresh_token()
    {
        const string password = "Browser-session-password-123!";
        var user = new User($"browser-session-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password));
        var tenant = new Tenant($"Browser session {Guid.NewGuid():N}");
        var membership = new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true);
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, tenant, membership);
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["Cors:AllowedOrigins:0"] = "https://frontend.example.test",
                ["Cors:AllowCredentials"] = "true",
                ["WebSession:AllowLegacyBodyTransport"] = "false",
                ["WebSession:PublicApiOrigin"] = "https://api.example.test",
                ["WebSession:SiteDomain"] = "example.test",
                ["SecurityEvents:Fingerprinting:ActiveKeyVersion"] = "staging-test",
                ["SecurityEvents:Fingerprinting:Keys:0:Version"] = "staging-test",
                ["SecurityEvents:Fingerprinting:Keys:0:Secret"] = "staging-test-security-fingerprint-key-32-bytes"
            }, environment: "Staging");
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
        using (var scope = factory.Services.CreateScope())
            Assert.False(scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<WebSessionOptions>>()
                .Value.AllowLegacyBodyTransport);

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginBody = await login.Content.ReadAsStringAsync();
        Assert.DoesNotContain("refreshToken", loginBody, StringComparison.OrdinalIgnoreCase);
        var loginCookies = login.Headers.GetValues("Set-Cookie").ToArray();
        var firstRefreshCookie = Assert.Single(loginCookies, value => value.StartsWith("__Host-Shine.Refresh=", StringComparison.Ordinal));
        Assert.Contains("httponly", firstRefreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", firstRefreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", firstRefreshCookie, StringComparison.OrdinalIgnoreCase);
        var csrf = CookieValue(loginCookies, WebSessionTransport.AntiforgeryRequestCookieName);

        client.DefaultRequestHeaders.Add(WebSessionTransport.AntiforgeryHeaderName, csrf);
        var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.DoesNotContain("refreshToken", await refresh.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        var rotatedCookies = refresh.Headers.GetValues("Set-Cookie").ToArray();
        var secondRefreshCookie = Assert.Single(rotatedCookies, value => value.StartsWith("__Host-Shine.Refresh=", StringComparison.Ordinal));
        Assert.NotEqual(CookieValue([firstRefreshCookie], "__Host-Shine.Refresh"),
            CookieValue([secondRefreshCookie], "__Host-Shine.Refresh"));

        client.DefaultRequestHeaders.Remove(WebSessionTransport.AntiforgeryHeaderName);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync("/api/auth/refresh", new Dictionary<string, string>())).StatusCode);

        client.DefaultRequestHeaders.Add(WebSessionTransport.AntiforgeryHeaderName,
            CookieValue(rotatedCookies, WebSessionTransport.AntiforgeryRequestCookieName));
        var logout = await client.PostAsJsonAsync("/api/auth/logout", new Dictionary<string, string>());
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.Contains(logout.Headers.GetValues("Set-Cookie"), value =>
            value.StartsWith("__Host-Shine.Refresh=", StringComparison.Ordinal) &&
            value.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Recovery_is_non_enumerable_correlatable_and_absent_from_audit()
    {
        var startedAtUtc = DateTime.UtcNow;
        var knownEmail = $"known-recovery-{Guid.NewGuid():N}@example.test";
        var unknownEmail = $"unknown-recovery-{Guid.NewGuid():N}@example.test";
        var user = new User(knownEmail, "irrelevant-hash");
        await using (var setup = fixture.CreateDb())
        {
            setup.Users.Add(user);
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var known = await client.PostAsJsonAsync("/api/auth/password/recovery", new PasswordRecoveryRequest(knownEmail));
        var unknown = await client.PostAsJsonAsync("/api/auth/password/recovery", new PasswordRecoveryRequest(unknownEmail));

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        await using var verification = fixture.CreateDb();
        var logs = await verification.OperationalLogs.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= startedAtUtc &&
                x.Message.Contains(AuthenticationSecurityEventNames.PasswordResetRequested))
            .Select(x => x.Message)
            .ToArrayAsync();
        Assert.Equal(2, logs.Length);
        var envelopes = logs.Select(Parse).ToArray();
        Assert.All(envelopes, envelope =>
        {
            Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("correlationId").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(envelope.GetProperty("subjectFingerprint").GetString()));
        });
        Assert.NotEqual(envelopes[0].GetProperty("subjectFingerprint").GetString(),
            envelopes[1].GetProperty("subjectFingerprint").GetString());
        Assert.DoesNotContain(logs, log => log.Contains(knownEmail, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(logs, log => log.Contains(unknownEmail, StringComparison.OrdinalIgnoreCase));
        var audits = await verification.AuditEntries.AsNoTracking()
            .Where(x => x.OccurredAtUtc >= startedAtUtc && x.EntityType == "AuthenticationSecurityEvent")
            .Select(x => x.NewValuesJson)
            .ToArrayAsync();
        Assert.DoesNotContain(audits, payload => payload is not null &&
            Parse(payload).GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.PasswordResetRequested);
    }

    [Fact]
    public async Task Refresh_matrix_has_correct_outcomes_routing_and_no_raw_tokens()
    {
        var startedAtUtc = DateTime.UtcNow;
        var user = new User($"refresh-matrix-{Guid.NewGuid():N}@example.test", "hash");
        var validRaw = $"valid-{Guid.NewGuid():N}";
        var expiredRaw = $"expired-{Guid.NewGuid():N}";
        var revokedRaw = $"revoked-{Guid.NewGuid():N}";
        var reusedRaw = $"reused-{Guid.NewGuid():N}";
        var valid = new RefreshToken(user.Id, null, null, RefreshTokenHash.Hash(validRaw), DateTime.UtcNow.AddHours(1));
        var expired = new RefreshToken(user.Id, null, null, RefreshTokenHash.Hash(expiredRaw), DateTime.UtcNow.AddMinutes(-1));
        var revoked = new RefreshToken(user.Id, null, null, RefreshTokenHash.Hash(revokedRaw), DateTime.UtcNow.AddHours(1));
        revoked.Revoke(DateTime.UtcNow);
        var reused = new RefreshToken(user.Id, null, null, RefreshTokenHash.Hash(reusedRaw), DateTime.UtcNow.AddHours(1));
        reused.Revoke(DateTime.UtcNow, RefreshTokenHash.Hash($"replacement-{Guid.NewGuid():N}"));
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, valid, expired, revoked, reused);
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var accepted = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(validRaw));
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var replacement = Assert.IsType<RefreshResponse>(await accepted.Content.ReadFromJsonAsync<RefreshResponse>());
        foreach (var raw in new[] { expiredRaw, revokedRaw, reusedRaw })
            Assert.Equal(HttpStatusCode.Unauthorized,
                (await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(raw))).StatusCode);

        await using var verification = fixture.CreateDb();
        var logs = await verification.OperationalLogs.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= startedAtUtc && x.Message.Contains(user.Id.ToString()) &&
                (x.Message.Contains(AuthenticationSecurityEventNames.SessionRefresh) ||
                 x.Message.Contains(AuthenticationSecurityEventNames.SessionReuseDetected)))
            .Select(x => x.Message)
            .ToArrayAsync();
        Assert.Equal(5, logs.Length);
        var refreshEvents = logs.Select(Parse)
            .Where(x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.SessionRefresh)
            .ToArray();
        Assert.Equal(4, refreshEvents.Length);
        Assert.Single(refreshEvents, x => x.GetProperty("outcome").GetString() == AuthenticationSecurityOutcomes.Succeeded);
        Assert.Contains(refreshEvents, x => x.GetProperty("reason").GetString() == AuthenticationSecurityReasons.TokenExpired);
        Assert.Equal(AuthenticationSecurityOutcomes.Detected,
            Assert.Single(logs.Select(Parse), x => x.GetProperty("eventName").GetString() ==
                AuthenticationSecurityEventNames.SessionReuseDetected).GetProperty("outcome").GetString());
        foreach (var secret in new[] { validRaw, expiredRaw, revokedRaw, reusedRaw, replacement.RefreshToken! })
            Assert.DoesNotContain(logs, log => log.Contains(secret, StringComparison.Ordinal));

        var audits = (await verification.AuditEntries.AsNoTracking()
                .Where(x => x.OccurredAtUtc >= startedAtUtc && x.EntityType == "AuthenticationSecurityEvent")
                .Select(x => x.NewValuesJson)
                .ToArrayAsync())
            .Where(x => x is not null && x.Contains(user.Id.ToString()))
            .Select(x => Parse(x!))
            .Where(x => x.GetProperty("eventName").GetString() is
                AuthenticationSecurityEventNames.SessionRefresh or AuthenticationSecurityEventNames.SessionReuseDetected)
            .ToArray();
        Assert.Single(audits);
        Assert.Equal(AuthenticationSecurityEventNames.SessionReuseDetected,
            audits[0].GetProperty("eventName").GetString());
    }

    [Fact]
    public async Task Session_password_and_tenant_mutations_are_audited_without_cross_tenant_enrichment()
    {
        const string oldPassword = "Old-password-123!";
        const string newPassword = "New-password-456!";
        var startedAtUtc = DateTime.UtcNow;
        var user = new User($"mutation-matrix-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(oldPassword));
        var allowed = new Tenant($"Allowed {Guid.NewGuid():N}");
        var forbidden = new Tenant($"Forbidden {Guid.NewGuid():N}");
        var membership = new UserTenant(user.Id, allowed.Id, user.Id, isOwner: true);
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, allowed, forbidden, membership);
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var loginHttp = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, oldPassword));
        var login = Assert.IsType<LoginResponse>(await loginHttp.Content.ReadFromJsonAsync<LoginResponse>());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);

        var selectedHttp = await client.PostAsJsonAsync("/api/auth/select-tenant",
            new SelectTenantRequest(allowed.Id, login.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, selectedHttp.StatusCode);
        var selected = Assert.IsType<RefreshResponse>(await selectedHttp.Content.ReadFromJsonAsync<RefreshResponse>());
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/auth/select-tenant",
                new SelectTenantRequest(forbidden.Id, selected.RefreshToken))).StatusCode);

        Guid loginSessionId;
        await using (var lookup = fixture.CreateDb())
            loginSessionId = (await lookup.RefreshTokens.AsNoTracking()
                .SingleAsync(x => x.TokenHash == RefreshTokenHash.Hash(login.RefreshToken!))).Id;
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/auth/sessions/{loginSessionId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/logout", new RefreshRequest(selected.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout-all", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/change-password", new ChangePasswordRequest(oldPassword, newPassword))).StatusCode);

        await using var verification = fixture.CreateDb();
        var logs = await verification.OperationalLogs.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= startedAtUtc && x.Message.Contains(user.Id.ToString()))
            .Select(x => x.Message)
            .ToArrayAsync();
        var relevant = logs.Select(Parse).Where(x => x.GetProperty("eventName").GetString() is
            AuthenticationSecurityEventNames.TenantSelection or AuthenticationSecurityEventNames.SessionRevoked or
            AuthenticationSecurityEventNames.PasswordChanged).ToArray();
        Assert.Contains(relevant, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.TenantSelection &&
            x.GetProperty("outcome").GetString() == AuthenticationSecurityOutcomes.Succeeded);
        var deniedSelection = Assert.Single(relevant, x => x.GetProperty("eventName").GetString() ==
            AuthenticationSecurityEventNames.TenantSelection && x.GetProperty("outcome").GetString() == AuthenticationSecurityOutcomes.Denied);
        Assert.False(deniedSelection.TryGetProperty("tenantId", out _));
        Assert.DoesNotContain(logs, log => log.Contains(forbidden.Id.ToString(), StringComparison.OrdinalIgnoreCase));
        Assert.True(relevant.Count(x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.SessionRevoked) >= 3);
        Assert.Single(relevant, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.PasswordChanged &&
            x.GetProperty("outcome").GetString() == AuthenticationSecurityOutcomes.Succeeded);
        foreach (var secret in new[] { oldPassword, newPassword, login.RefreshToken!, selected.RefreshToken! })
            Assert.DoesNotContain(logs, log => log.Contains(secret, StringComparison.Ordinal));

        var auditPayloads = await verification.AuditEntries.AsNoTracking()
            .Where(x => x.OccurredAtUtc >= startedAtUtc && x.EntityType == "AuthenticationSecurityEvent" &&
                (x.UserId == user.Id || x.EntityId == user.Id.ToString()))
            .Select(x => x.NewValuesJson!)
            .ToArrayAsync();
        Assert.True(auditPayloads.Length >= relevant.Length);
    }

    [Fact]
    public async Task Telemetry_failure_does_not_replace_the_rate_limit_response()
    {
        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString,
            new Dictionary<string, string?>
            {
                ["AuthenticationRateLimiting:Registration:PermitLimit"] = "1",
                ["AuthenticationRateLimiting:Registration:Window"] = "00:01:00"
            }, securityEventEmitter: new ThrowingSecurityEventEmitter());
        using var client = factory.CreateClient();
        var request = new RegistrationRequest("", "", "", "");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.PostAsJsonAsync("/api/auth/register", request)).StatusCode);
    }

    [Fact]
    public async Task Lockout_transitions_and_password_reset_are_audited_through_http_without_secrets()
    {
        const string password = "Current-password-123!";
        const string replacementPassword = "Replacement-password-456!";
        var startedAtUtc = DateTime.UtcNow;
        var newlyLocked = new User($"new-lockout-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password));
        var expiredLock = new User($"expired-lockout-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password));
        expiredLock.RegisterFailedLogin(DateTime.UtcNow.AddMinutes(-2), 1, TimeSpan.FromMinutes(1));
        var tenant = new Tenant($"Expired lock tenant {Guid.NewGuid():N}");
        var membership = new UserTenant(expiredLock.Id, tenant.Id, expiredLock.Id, isOwner: true);
        var resetUser = new User($"reset-http-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password));
        var resetRaw = $"reset-secret-{Guid.NewGuid():N}";
        var resetHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(resetRaw)));
        var reset = new PasswordResetToken(resetUser.Id, resetHash, DateTime.UtcNow.AddMinutes(10));
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(newlyLocked, expiredLock, tenant, membership, resetUser, reset);
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString,
            new Dictionary<string, string?> { ["LoginSecurity:MaxFailedAttempts"] = "1" });
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(newlyLocked.Email, "wrong-password-marker"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(expiredLock.Email, password))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await client.PostAsJsonAsync("/api/auth/password/reset",
                new PasswordResetRequest(resetRaw, replacementPassword))).StatusCode);

        await using var verification = fixture.CreateDb();
        var logs = await verification.OperationalLogs.AsNoTracking()
            .Where(x => x.CreatedAtUtc >= startedAtUtc &&
                (x.Message.Contains(newlyLocked.Id.ToString()) || x.Message.Contains(expiredLock.Id.ToString()) ||
                 x.Message.Contains(resetUser.Id.ToString())))
            .Select(x => x.Message)
            .ToArrayAsync();
        var envelopes = logs.Select(Parse).ToArray();
        Assert.Single(envelopes, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.LockoutStarted);
        Assert.Single(envelopes, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.LockoutEnded);
        Assert.Single(envelopes, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.PasswordResetCompleted &&
            x.GetProperty("outcome").GetString() == AuthenticationSecurityOutcomes.Succeeded);
        foreach (var secret in new[] { newlyLocked.Email, expiredLock.Email, resetUser.Email, "wrong-password-marker", password,
                     replacementPassword, resetRaw })
            Assert.DoesNotContain(logs, log => log.Contains(secret, StringComparison.OrdinalIgnoreCase));

        var audits = (await verification.AuditEntries.AsNoTracking()
                .Where(x => x.OccurredAtUtc >= startedAtUtc && x.EntityType == "AuthenticationSecurityEvent")
                .Select(x => x.NewValuesJson)
                .ToArrayAsync())
            .Where(x => x is not null && (x.Contains(newlyLocked.Id.ToString()) || x.Contains(expiredLock.Id.ToString()) ||
                                         x.Contains(resetUser.Id.ToString())))
            .Select(x => Parse(x!))
            .ToArray();
        Assert.Contains(audits, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.LockoutStarted);
        Assert.Contains(audits, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.LockoutEnded);
        Assert.Contains(audits, x => x.GetProperty("eventName").GetString() == AuthenticationSecurityEventNames.PasswordResetCompleted);
    }

    [Fact]
    public async Task Sid_identifies_the_exact_session_across_login_refresh_selection_and_revocation()
    {
        const string password = "Session-password-123!";
        var user = new User($"sid-session-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password));
        var tenant = new Tenant($"Sid tenant {Guid.NewGuid():N}");
        var membership = new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true);
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, tenant, membership);
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var first = await Login(client, user.Email, password);
        var second = await Login(client, user.Email, password);
        var firstSid = Sid(first.AccessToken);
        var secondSid = Sid(second.AccessToken);
        Assert.NotEqual(firstSid, secondSid);

        await using (var verification = fixture.CreateDb())
        {
            Assert.Equal(firstSid, (await verification.RefreshTokens.AsNoTracking()
                .SingleAsync(x => x.TokenHash == RefreshTokenHash.Hash(first.RefreshToken!))).Id);
            Assert.Equal(secondSid, (await verification.RefreshTokens.AsNoTracking()
                .SingleAsync(x => x.TokenHash == RefreshTokenHash.Hash(second.RefreshToken!))).Id);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", first.AccessToken);
        Assert.Equal(firstSid, Assert.IsType<SessionResponse>(await (await client.GetAsync("/api/auth/sessions/current"))
            .Content.ReadFromJsonAsync<SessionResponse>()).Id);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/auth/sessions/{secondSid}")).StatusCode);
        Assert.Equal(firstSid, Assert.IsType<SessionResponse>(await (await client.GetAsync("/api/auth/sessions/current"))
            .Content.ReadFromJsonAsync<SessionResponse>()).Id);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", second.AccessToken);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/auth/sessions/current")).StatusCode);

        var refreshedHttp = await client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest(first.RefreshToken));
        var refreshed = Assert.IsType<RefreshResponse>(await refreshedHttp.Content.ReadFromJsonAsync<RefreshResponse>());
        var refreshedSid = Sid(refreshed.AccessToken);
        Assert.NotEqual(firstSid, refreshedSid);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", refreshed.AccessToken);
        var selectedHttp = await client.PostAsJsonAsync("/api/auth/select-tenant",
            new SelectTenantRequest(tenant.Id, refreshed.RefreshToken));
        var selected = Assert.IsType<RefreshResponse>(await selectedHttp.Content.ReadFromJsonAsync<RefreshResponse>());
        var selectedSid = Sid(selected.AccessToken);
        await using var finalVerification = fixture.CreateDb();
        Assert.Equal(selectedSid, (await finalVerification.RefreshTokens.AsNoTracking()
            .SingleAsync(x => x.TokenHash == RefreshTokenHash.Hash(selected.RefreshToken!))).Id);
    }

    [Fact]
    public async Task Concurrent_tenant_selection_rotates_one_session_exactly_once()
    {
        const string password = "Tenant-selection-password-123!";
        var user = new User($"tenant-selection-race-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password));
        var tenant = new Tenant($"Tenant selection race {Guid.NewGuid():N}");
        var membership = new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true);
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, tenant, membership);
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var firstClient = factory.CreateClient();
        using var secondClient = factory.CreateClient();
        var login = await Login(firstClient, user.Email, password);
        firstClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        secondClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        var selection = new SelectTenantRequest(tenant.Id, login.RefreshToken);

        var responses = await Task.WhenAll(
            firstClient.PostAsJsonAsync("/api/auth/select-tenant", selection),
            secondClient.PostAsJsonAsync("/api/auth/select-tenant", selection));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Unauthorized);
        await using var verification = fixture.CreateDb();
        Assert.Single(await verification.RefreshTokens.AsNoTracking()
            .Where(token => token.UserId == user.Id && token.RevokedAtUtc == null)
            .ToArrayAsync());
    }

    [Fact]
    public async Task Registration_requires_email_confirmation_before_creating_the_first_contextual_session()
    {
        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var organizationName = $"Organization {Guid.NewGuid():N}";
        var unitName = $"Unit {Guid.NewGuid():N}";
        var email = $"sid-registration-{Guid.NewGuid():N}@example.test";
        var response = await client.PostAsJsonAsync("/api/auth/register", new RegistrationRequest(
            email, "Registration-password-123!",
            organizationName, unitName));
        Assert.True(response.StatusCode == HttpStatusCode.Accepted,
            $"Expected registration to succeed, got {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        await using var verification = fixture.CreateDb();
        var user = await verification.Users.SingleAsync(x => x.NormalizedEmail == User.NormalizeEmail(email));
        Assert.False(user.IsEmailVerified);
        Assert.False(await verification.RefreshTokens.AnyAsync(x => x.UserId == user.Id));
        var organization = await verification.CustomerAccounts.AsNoTracking()
            .SingleAsync(account => account.Name == organizationName);
        Assert.Equal(organizationName, organization.Name);
        var unit = await verification.Tenants.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(candidate => candidate.Name == unitName);
        Assert.Equal(unitName, unit.Name);
        Assert.Equal(organization.Id, unit.CustomerAccountId);
        Assert.True(await verification.CustomerAccountUsers.AsNoTracking().AnyAsync(membership =>
            membership.AccountId == organization.Id && membership.UserId == user.Id));
        var roles = await verification.UserTenantRoles.IgnoreQueryFilters().AsNoTracking()
            .Where(link => link.UserId == user.Id && link.TenantId == unit.Id)
            .Select(link => link.Role.Name)
            .ToArrayAsync();
        Assert.Contains(Role.OwnerName, roles);
        Assert.Contains(Role.AdministratorName, roles);
        var tenantPlan = await verification.TenantPlans.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(plan => plan.TenantId == unit.Id);
        var defaultPlan = await verification.Plans.AsNoTracking().Include(plan => plan.Modules)
            .SingleAsync(plan => plan.Id == tenantPlan.PlanId);
        Assert.Equal("DEFAULT", defaultPlan.Code);
        Assert.Contains(defaultPlan.Modules, module => module.ModuleCode == "SCHEDULING");

        var delivery = Assert.IsType<InMemoryEmailVerificationDelivery>(
            factory.Services.GetRequiredService<IEmailVerificationDelivery>());
        Assert.True(delivery.TryGetLatest(email, out var message));
        var token = System.Net.WebUtility.UrlDecode(
            System.Text.RegularExpressions.Regex.Match(message!.TextBody, @"token=([^\s]+)").Groups[1].Value);
        var confirmedHttp = await client.PostAsJsonAsync("/api/auth/email-verification/confirm",
            new EmailVerificationConfirmRequest(token));
        Assert.Equal(HttpStatusCode.OK, confirmedHttp.StatusCode);
        var confirmed = Assert.IsType<EmailVerificationConfirmationResponse>(
            await confirmedHttp.Content.ReadFromJsonAsync<EmailVerificationConfirmationResponse>());
        Assert.Equal(unit.Id, confirmed.UnitId);
        Assert.Equal(Sid(confirmed.AccessToken), (await verification.RefreshTokens.AsNoTracking()
            .SingleAsync(x => x.UserId == user.Id)).Id);
    }

    [Fact]
    public async Task Authentication_failures_publish_stable_frontend_error_envelopes()
    {
        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        await AssertError(client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest($"unknown-{Guid.NewGuid():N}@example.test", "wrong")),
            HttpStatusCode.Unauthorized, AuthenticationErrors.InvalidCredentials);
        await AssertError(client.PostAsJsonAsync("/api/auth/refresh", new RefreshRequest("invalid-refresh")),
            HttpStatusCode.Unauthorized, AuthenticationErrors.SessionInvalid);
        await AssertError(client.PostAsJsonAsync("/api/auth/password/reset",
            new PasswordResetRequest("invalid-reset", "Valid-replacement-password-123!")),
            HttpStatusCode.BadRequest, AuthenticationErrors.PasswordResetInvalid);
        await AssertError(client.GetAsync("/api/auth/sessions"),
            HttpStatusCode.Unauthorized, AuthenticationErrors.SessionInvalid);

        var weakRegistration = new RegistrationRequest(
            $"weak-{Guid.NewGuid():N}@example.test", "weak", "Organization", "Unit");
        await AssertError(client.PostAsJsonAsync("/api/auth/register", weakRegistration),
            HttpStatusCode.BadRequest, AuthenticationErrors.PasswordPolicyFailed, "password");

        var invalidNames = new RegistrationRequest(
            $"names-{Guid.NewGuid():N}@example.test", "Registration-password-123!", "", "");
        var invalidNamesResponse = await client.PostAsJsonAsync("/api/auth/register", invalidNames);
        Assert.Equal(HttpStatusCode.BadRequest, invalidNamesResponse.StatusCode);
        var invalidNameErrors = (await invalidNamesResponse.Content.ReadFromJsonAsync<AuthenticationErrorResponse>())!.Errors;
        Assert.Contains(invalidNameErrors, error => error.Code == AuthenticationErrors.OrganizationNameInvalid &&
            error.Field == "organizationName");
        Assert.Contains(invalidNameErrors, error => error.Code == AuthenticationErrors.UnitNameInvalid &&
            error.Field == "unitName");

        var email = $"duplicate-{Guid.NewGuid():N}@example.test";
        var registration = new RegistrationRequest(email, "Registration-password-123!", "Organization", "Unit");
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/auth/register", registration)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/auth/register", registration)).StatusCode);
    }

    [Fact]
    public async Task Unverified_identity_cannot_authenticate_and_concurrent_confirmation_creates_one_session()
    {
        const string password = "Email-verification-password-123!";
        var user = new User($"unverified-{Guid.NewGuid():N}@example.test",
            new Pbkdf2PasswordHashService().Hash(password), emailVerified: false);
        var tenant = new Tenant($"Unverified unit {Guid.NewGuid():N}");
        var membership = new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true);
        var challenge = EmailVerificationTokenService.Create();
        await using (var setup = fixture.CreateDb())
        {
            setup.AddRange(user, tenant, membership,
                new EmailVerificationToken(user.Id, challenge.TokenHash, DateTime.UtcNow.AddMinutes(30), DateTime.UtcNow));
            await setup.SaveChangesAsync();
        }

        await using var factory = new SecurityPipelineTests.ApiFactory(fixture.ConnectionString);
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await first.PostAsJsonAsync("/api/auth/login", new LoginRequest(user.Email, password))).StatusCode);

        var confirmations = await Task.WhenAll(
            first.PostAsJsonAsync("/api/auth/email-verification/confirm", new EmailVerificationConfirmRequest(challenge.RawToken)),
            second.PostAsJsonAsync("/api/auth/email-verification/confirm", new EmailVerificationConfirmRequest(challenge.RawToken)));
        Assert.Single(confirmations, response => response.StatusCode == HttpStatusCode.OK);
        Assert.Single(confirmations, response => response.StatusCode == HttpStatusCode.BadRequest);
        await using var verification = fixture.CreateDb();
        Assert.Single(await verification.RefreshTokens.AsNoTracking().Where(x => x.UserId == user.Id).ToArrayAsync());
        Assert.True((await verification.Users.AsNoTracking().SingleAsync(x => x.Id == user.Id)).IsEmailVerified);
    }

    private static async Task<LoginResponse> Login(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return Assert.IsType<LoginResponse>(await response.Content.ReadFromJsonAsync<LoginResponse>());
    }

    private static Guid Sid(string accessToken) => Guid.Parse(new JwtSecurityTokenHandler().ReadJwtToken(accessToken)
        .Claims.Single(x => x.Type == "sid").Value);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static async Task AssertError(
        Task<HttpResponseMessage> responseTask,
        HttpStatusCode status,
        string code,
        string? field = null)
    {
        using var response = await responseTask;
        Assert.Equal(status, response.StatusCode);
        var envelope = Assert.IsType<AuthenticationErrorResponse>(
            await response.Content.ReadFromJsonAsync<AuthenticationErrorResponse>());
        var errors = envelope.Errors.Where(candidate => candidate.Code == code).ToArray();
        Assert.NotEmpty(errors);
        Assert.All(errors, error => Assert.Equal(field, error.Field));
    }

    private static string CookieValue(IEnumerable<string> setCookieHeaders, string name)
    {
        var header = setCookieHeaders.Single(value => value.StartsWith($"{name}=", StringComparison.Ordinal));
        return header[(name.Length + 1)..].Split(';', 2)[0];
    }

    private sealed class ThrowingSecurityEventEmitter : IAuthenticationSecurityEventEmitter
    {
        public AuthenticationSecurityEventEnvelope Enqueue(AuthenticationSecurityEventInput input) =>
            throw new InvalidOperationException("Synthetic telemetry failure.");
    }
}
