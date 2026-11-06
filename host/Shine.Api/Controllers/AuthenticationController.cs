using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;
using System.Text.Json.Serialization;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(
    ShineDbContext dbContext,
    IPasswordHashService passwordHashService,
    IPasswordPolicy passwordPolicy,
    IAccessTokenService accessTokenService,
    IOptions<JwtOptions> jwtOptions,
    IOptions<LoginSecurityOptions> loginSecurity,
    IAuthenticationSecurityEventEmitter securityEvents,
    IWebSessionTransport webSession,
    TenantSessionSelectionService tenantSelection) : ControllerBase
{
    [HttpGet("csrf")]
    [AllowAnonymous]
    public IActionResult Csrf()
    {
        webSession.IssueAntiforgeryToken(HttpContext);
        return NoContent();
    }

    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        var credential = ReadRefreshToken(request.RefreshToken);
        if (credential is not null && !await ValidateAntiforgeryAsync(credential))
            return AuthenticationErrors.Result(400, AuthenticationErrors.CsrfValidationFailed,
                "Não foi possível validar a origem da requisição.");
        if (credential is null)
        {
            Enqueue(AuthenticationSecurityEventNames.SessionRevoked, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.ValidationFailed);
            await dbContext.SaveChangesAsync(cancellationToken);
            webSession.ClearSession(HttpContext);
            return NoContent();
        }

        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(item => item.TokenHash == RefreshTokenHash.Hash(credential.RawToken), cancellationToken);
        if (token is not null && token.IsActive(DateTime.UtcNow))
        {
            token.Revoke(DateTime.UtcNow);
            Enqueue(AuthenticationSecurityEventNames.SessionRevoked, AuthenticationSecurityOutcomes.Succeeded,
                AuthenticationSecurityReasons.SessionRevoked, token.UserId, token.UserId, token.Id, token.TenantId);
        }
        else
        {
            Enqueue(AuthenticationSecurityEventNames.SessionRevoked, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.TokenRevoked);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        webSession.ClearSession(HttpContext);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue("user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");

        var now = DateTime.UtcNow;
        var sessions = await dbContext.RefreshTokens
            .Where(item => item.UserId == userId && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);

        Enqueue(AuthenticationSecurityEventNames.SessionRevoked, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.SessionRevoked, userId, userId,
            tenantId: Guid.TryParse(User.FindFirstValue("tenant_id"), out var tenantId) ? tenantId : null);

        await dbContext.SaveChangesAsync(cancellationToken);
        webSession.ClearSession(HttpContext);
        return NoContent();
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyCollection<SessionResponse>>> Sessions(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue("user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");

        var sessions = await dbContext.RefreshTokens
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new SessionResponse(
                item.Id,
                item.TenantId,
                item.UserTenantId,
                item.CreatedAtUtc,
                item.ExpiresAtUtc,
                item.RevokedAtUtc,
                item.IsActive(DateTime.UtcNow)))
            .ToArrayAsync(cancellationToken);

        return Ok(sessions);
    }

    [Authorize]
    [HttpGet("sessions/current")]
    public async Task<ActionResult<SessionResponse>> CurrentSession(CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var tenantId = Guid.TryParse(User.FindFirstValue("tenant_id"), out var parsedTenantId) ? parsedTenantId : (Guid?)null;
        var sessionId = Guid.TryParse(User.FindFirstValue("sid"), out var parsedSessionId) ? parsedSessionId : (Guid?)null;
        var session = await dbContext.RefreshTokens.AsNoTracking()
            .Where(item => item.UserId == userId && item.TenantId == tenantId && item.RevokedAtUtc == null &&
                item.ExpiresAtUtc > DateTime.UtcNow && (sessionId == null || item.Id == sessionId))
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new SessionResponse(item.Id, item.TenantId, item.UserTenantId, item.CreatedAtUtc, item.ExpiresAtUtc, item.RevokedAtUtc, item.IsActive(DateTime.UtcNow)))
            .FirstOrDefaultAsync(cancellationToken);
        return session is null ? NotFound() : Ok(session);
    }

    [Authorize]
    [HttpDelete("sessions/{sessionId:guid}")]
    public async Task<IActionResult> RevokeSession(Guid sessionId, CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue("user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId))
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");

        var session = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(item => item.Id == sessionId && item.UserId == userId, cancellationToken);
        if (session is null)
        {
            Enqueue(AuthenticationSecurityEventNames.SessionRevoked, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.SessionRevoked, userId, userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return AuthenticationErrors.Result(404, AuthenticationErrors.SessionNotFound, "A sessão não foi encontrada.");
        }
        if (session.IsActive(DateTime.UtcNow)) session.Revoke(DateTime.UtcNow);
        Enqueue(AuthenticationSecurityEventNames.SessionRevoked, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.SessionRevoked, userId, userId, session.Id, session.TenantId);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.Login)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            Enqueue(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.ValidationFailed, subjectIdentifier: request.Email);
            await dbContext.SaveChangesAsync(cancellationToken);
            return AuthenticationErrors.Result(401, AuthenticationErrors.InvalidCredentials, "E-mail ou senha inválidos.");
        }

        var user = await dbContext.Users.Include(candidate => candidate.Tenants).ThenInclude(link => link.Tenant).SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == Shine.Domain.Identity.User.NormalizeEmail(request.Email), cancellationToken);

        var now = DateTime.UtcNow;
        var passwordIsValid = passwordHashService.Verify(request.Password, user?.PasswordHash);
        var wasLocked = user?.LockedUntilUtc is not null;
        if (user is null || !user.IsActive || !user.IsEmailVerified || user.IsLoginLocked(now) || !passwordIsValid)
        {
            var lockedBeforeAttempt = user?.IsLoginLocked(now) == true;
            if (user is not null && user.IsActive && user.IsEmailVerified && !user.IsLoginLocked(now))
                user.RegisterFailedLogin(now, loginSecurity.Value.MaxFailedAttempts, TimeSpan.FromMinutes(loginSecurity.Value.LockoutMinutes));
            var lockedByAttempt = !lockedBeforeAttempt && user?.IsLoginLocked(now) == true;
            var reason = user is null ? AuthenticationSecurityReasons.InvalidCredentials :
                !user.IsActive ? AuthenticationSecurityReasons.AccountInactive :
                lockedBeforeAttempt ? AuthenticationSecurityReasons.AccountLocked : AuthenticationSecurityReasons.InvalidCredentials;
            Enqueue(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Denied, reason,
                subjectUserId: user?.Id, subjectIdentifier: request.Email);
            if (lockedByAttempt)
                Enqueue(AuthenticationSecurityEventNames.LockoutStarted, AuthenticationSecurityOutcomes.Detected,
                    AuthenticationSecurityReasons.AccountLocked, subjectUserId: user!.Id, subjectIdentifier: request.Email);
            await dbContext.SaveChangesAsync(cancellationToken);
            return AuthenticationErrors.Result(401, AuthenticationErrors.InvalidCredentials, "E-mail ou senha inválidos.");
        }
        user.RegisterSuccessfulLogin();

        var accessibleLinks = user.Tenants.Where(link => link.IsActive && link.Tenant.IsActive).ToArray();
        if (accessibleLinks.Length == 0)
        {
            Enqueue(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.TenantNotAllowed, subjectUserId: user.Id, subjectIdentifier: request.Email);
            await dbContext.SaveChangesAsync(cancellationToken);
            return AuthenticationErrors.Result(401, AuthenticationErrors.InvalidCredentials, "E-mail ou senha inválidos.");
        }

        var tenants = accessibleLinks
            .Select(link => new AccessibleTenantResponse(link.Tenant.Id, link.Tenant.Name)).ToArray();
        Guid? tenantId = null;
        Guid? userTenantId = null;
        IReadOnlyCollection<string> roles = [];
        if (accessibleLinks is [{ } singleLink])
        {
            tenantId = singleLink.TenantId;
            userTenantId = singleLink.UserTenantId;
            roles = await dbContext.UserTenantRoles
                .AsNoTracking()
                .Where(item => item.UserId == user.Id && item.TenantId == singleLink.TenantId)
                .Select(item => item.Role.Name)
                .Distinct()
                .ToArrayAsync(cancellationToken);
        }
        var refresh = RefreshTokenHash.Create();
        var session = new RefreshToken(user.Id, tenantId, userTenantId, refresh.Hash, DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays));
        dbContext.RefreshTokens.Add(session);
        var accessToken = accessTokenService.Create(user.Id, session.Id, tenantId, userTenantId, roles);
        if (wasLocked)
            Enqueue(AuthenticationSecurityEventNames.LockoutEnded, AuthenticationSecurityOutcomes.Succeeded,
                AuthenticationSecurityReasons.AccountLocked, user.Id, user.Id, tenantId: tenantId, subjectIdentifier: request.Email);
        Enqueue(AuthenticationSecurityEventNames.Login, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.Unknown, user.Id, user.Id, session.Id, tenantId, request.Email);
        await dbContext.SaveChangesAsync(cancellationToken);
        webSession.WriteSession(HttpContext, refresh.Raw, session.ExpiresAtUtc);
        return Ok(new LoginResponse(user.Id, user.Email, tenants, accessToken.Token, accessToken.ExpiresAtUtc,
            ResponseRefreshToken(refresh.Raw)));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.Refresh)]
    public async Task<ActionResult<RefreshResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var credential = ReadRefreshToken(request.RefreshToken);
        if (credential is null)
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        if (!await ValidateAntiforgeryAsync(credential))
            return AuthenticationErrors.Result(400, AuthenticationErrors.CsrfValidationFailed,
                "Não foi possível validar a origem da requisição.");
        var tokenHash = RefreshTokenHash.Hash(credential.RawToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var current = await dbContext.RefreshTokens.FromSqlInterpolated($$"""
            SELECT * FROM "RefreshTokens" WHERE "TokenHash" = {{tokenHash}} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        var now = DateTime.UtcNow;
        if (current is null || !current.IsActive(now))
        {
            var reason = current is null ? AuthenticationSecurityReasons.TokenRevoked :
                current.ExpiresAtUtc <= now ? AuthenticationSecurityReasons.TokenExpired : AuthenticationSecurityReasons.TokenRevoked;
            Enqueue(AuthenticationSecurityEventNames.SessionRefresh, AuthenticationSecurityOutcomes.Denied, reason,
                subjectUserId: current?.UserId, sessionId: current?.Id, tenantId: current?.TenantId);
            if (current?.ReplacedByTokenHash is not null)
                Enqueue(AuthenticationSecurityEventNames.SessionReuseDetected, AuthenticationSecurityOutcomes.Detected,
                    AuthenticationSecurityReasons.TokenReuseDetected, subjectUserId: current.UserId,
                    sessionId: current.Id, tenantId: current.TenantId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        }
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == current.UserId, cancellationToken);
        if (user is null || !user.IsActive || !user.IsEmailVerified)
        {
            Enqueue(AuthenticationSecurityEventNames.SessionRefresh, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.AccountInactive, subjectUserId: current.UserId,
                sessionId: current.Id, tenantId: current.TenantId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        }
        if (current.TenantId is Guid refreshTenantId && !await dbContext.Tenants.AnyAsync(tenant => tenant.Id == refreshTenantId && tenant.IsActive, cancellationToken))
        {
            Enqueue(AuthenticationSecurityEventNames.SessionRefresh, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.TenantNotAllowed, subjectUserId: current.UserId,
                sessionId: current.Id, tenantId: current.TenantId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        }

        var replacement = RefreshTokenHash.Create();
        current.Revoke(DateTime.UtcNow, replacement.Hash);
        var next = new RefreshToken(current.UserId, current.TenantId, current.UserTenantId, replacement.Hash, DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays));
        dbContext.RefreshTokens.Add(next);
        var roles = current.TenantId is Guid tenantId
            ? await dbContext.UserTenantRoles.Where(item => item.UserId == current.UserId && item.TenantId == tenantId).Select(item => item.Role.Name).Distinct().ToArrayAsync(cancellationToken)
            : [];
        var access = accessTokenService.Create(current.UserId, next.Id, current.TenantId, current.UserTenantId, roles);
        Enqueue(AuthenticationSecurityEventNames.SessionRefresh, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.Unknown, current.UserId, current.UserId, next.Id, current.TenantId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        webSession.WriteSession(HttpContext, replacement.Raw, next.ExpiresAtUtc);
        return Ok(new RefreshResponse(access.Token, access.ExpiresAtUtc, ResponseRefreshToken(replacement.Raw)));
    }

    [Authorize]
    [HttpPost("select-tenant")]
    public async Task<ActionResult<TenantSelectionResponse>> SelectTenant(SelectTenantRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        var credential = ReadRefreshToken(request.RefreshToken);
        if (credential is null)
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        if (!await ValidateAntiforgeryAsync(credential))
            return AuthenticationErrors.Result(400, AuthenticationErrors.CsrfValidationFailed,
                "Não foi possível validar a origem da requisição.");
        var result = await tenantSelection.SelectAsync(userId, request.TenantId, credential.RawToken, cancellationToken);
        if (result.Outcome == TenantSessionSelectionOutcome.Unauthorized)
            return AuthenticationErrors.Result(401, AuthenticationErrors.SessionInvalid, "A sessão é inválida ou expirou.");
        if (result.Outcome == TenantSessionSelectionOutcome.Forbidden)
            return AuthenticationErrors.Result(403, AuthenticationErrors.UnitAccessDenied,
                "Você não possui acesso a esta unidade.");
        webSession.WriteSession(HttpContext, result.RefreshToken!, result.RefreshTokenExpiresAtUtc!.GetValueOrDefault());
        return Ok(new TenantSelectionResponse(
            result.TenantId!.Value,
            result.TenantName!,
            result.AccessToken!,
            result.AccessTokenExpiresAtUtc!.GetValueOrDefault(),
            ResponseRefreshToken(result.RefreshToken!)));
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (!passwordPolicy.IsValid(request.NewPassword, out var passwordErrors))
        {
            Enqueue(AuthenticationSecurityEventNames.PasswordChanged, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.ValidationFailed, userId, userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return BadRequest(AuthenticationErrors.PasswordPolicy(passwordErrors, "newPassword"));
        }
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || !passwordHashService.Verify(request.CurrentPassword, user.PasswordHash))
        {
            Enqueue(AuthenticationSecurityEventNames.PasswordChanged, AuthenticationSecurityOutcomes.Denied,
                user is not null && !user.IsActive ? AuthenticationSecurityReasons.AccountInactive : AuthenticationSecurityReasons.InvalidCredentials,
                userId, userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return AuthenticationErrors.Result(401, AuthenticationErrors.InvalidCredentials,
                "As credenciais informadas são inválidas.");
        }

        user.ChangePassword(passwordHashService.Hash(request.NewPassword));
        var now = DateTime.UtcNow;
        var sessions = await dbContext.RefreshTokens.Where(item => item.UserId == userId && item.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);
        Enqueue(AuthenticationSecurityEventNames.PasswordChanged, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.PasswordChanged, userId, userId);
        await dbContext.SaveChangesAsync(cancellationToken);
        webSession.ClearSession(HttpContext);
        return NoContent();
    }

    private RefreshTokenCredential? ReadRefreshToken(string? bodyToken) =>
        webSession.ReadRefreshToken(HttpContext, bodyToken);

    private Task<bool> ValidateAntiforgeryAsync(RefreshTokenCredential credential) =>
        webSession.ValidateAntiforgeryAsync(HttpContext, credential);

    private string? ResponseRefreshToken(string rawToken) => webSession.ResponseRefreshToken(rawToken);

    private Guid RequireUserId() => Guid.TryParse(User.FindFirstValue("user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new UnauthorizedAccessException("An authenticated user is required.");

    private void Enqueue(
        string eventName,
        string outcome,
        string reason,
        Guid? actorUserId = null,
        Guid? subjectUserId = null,
        Guid? sessionId = null,
        Guid? tenantId = null,
        string? subjectIdentifier = null)
    {
        var context = ControllerContext.HttpContext;
        securityEvents.Enqueue(new AuthenticationSecurityEventInput(
            eventName,
            outcome,
            reason,
            context?.TraceIdentifier ?? $"auth-{Guid.NewGuid():N}",
            actorUserId,
            subjectUserId,
            sessionId,
            tenantId,
            subjectIdentifier is { Length: <= 512 } ? subjectIdentifier : null,
            context?.Connection.RemoteIpAddress?.ToString(),
            context?.Request.Headers.UserAgent.ToString()));
    }
}

public sealed record RefreshRequest(string? RefreshToken = null);
public sealed record RefreshResponse(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken = null);
public sealed record SelectTenantRequest(Guid TenantId, string? RefreshToken = null);
public sealed record SessionResponse(Guid Id, Guid? TenantId, Guid? UserTenantId, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc, bool IsActive);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
