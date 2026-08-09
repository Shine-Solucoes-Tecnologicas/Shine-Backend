using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/auth")]
public sealed class AuthenticationController(ShineDbContext dbContext, IPasswordHashService passwordHashService, IPasswordPolicy passwordPolicy, IAccessTokenService accessTokenService, IOptions<JwtOptions> jwtOptions, IOptions<LoginSecurityOptions> loginSecurity) : ControllerBase
{
    [HttpPost("logout")]
    [AllowAnonymous]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken)) return NoContent();

        var token = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(item => item.TokenHash == RefreshTokenHash.Hash(request.RefreshToken), cancellationToken);
        if (token is not null && token.IsActive(DateTime.UtcNow))
            token.Revoke(DateTime.UtcNow);

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue("user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId)) return Unauthorized();

        var now = DateTime.UtcNow;
        var sessions = await dbContext.RefreshTokens
            .Where(item => item.UserId == userId && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);

        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [Authorize]
    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyCollection<SessionResponse>>> Sessions(CancellationToken cancellationToken)
    {
        var userIdValue = User.FindFirstValue("user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(userIdValue, out var userId)) return Unauthorized();

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
        var session = await dbContext.RefreshTokens.AsNoTracking()
            .Where(item => item.UserId == userId && item.TenantId == tenantId && item.RevokedAtUtc == null)
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
        if (!Guid.TryParse(userIdValue, out var userId)) return Unauthorized();

        var session = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(item => item.Id == sessionId && item.UserId == userId, cancellationToken);
        if (session is null) return NotFound();
        if (session.IsActive(DateTime.UtcNow)) session.Revoke(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized();

        var user = await dbContext.Users.Include(candidate => candidate.Tenants).ThenInclude(link => link.Tenant).SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == Shine.Domain.Identity.User.NormalizeEmail(request.Email), cancellationToken);

        var now = DateTime.UtcNow;
        if (user is null || !user.IsActive || user.IsLoginLocked(now) || !passwordHashService.Verify(request.Password, user.PasswordHash))
        {
            if (user is not null && user.IsActive && !user.IsLoginLocked(now))
                user.RegisterFailedLogin(now, loginSecurity.Value.MaxFailedAttempts, TimeSpan.FromMinutes(loginSecurity.Value.LockoutMinutes));
            await dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized();
        }
        user.RegisterSuccessfulLogin();

        var accessibleLinks = user.Tenants.Where(link => link.IsActive && link.Tenant.IsActive).ToArray();
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
        var accessToken = accessTokenService.Create(user.Id, tenantId, userTenantId, roles);
        var refresh = RefreshTokenHash.Create();
        dbContext.RefreshTokens.Add(new RefreshToken(user.Id, tenantId, userTenantId, refresh.Hash, DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new LoginResponse(user.Id, user.Email, tenants, accessToken.Token, accessToken.ExpiresAtUtc, refresh.Raw));
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<RefreshResponse>> Refresh(RefreshRequest request, CancellationToken cancellationToken)
    {
        var current = await dbContext.RefreshTokens.Include(item => item.User).SingleOrDefaultAsync(item => item.TokenHash == RefreshTokenHash.Hash(request.RefreshToken), cancellationToken);
        if (current is null || !current.IsActive(DateTime.UtcNow) || !current.User.IsActive) return Unauthorized();
        var replacement = RefreshTokenHash.Create();
        current.Revoke(DateTime.UtcNow, replacement.Hash);
        var next = new RefreshToken(current.UserId, current.TenantId, current.UserTenantId, replacement.Hash, DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays));
        dbContext.RefreshTokens.Add(next);
        var roles = current.TenantId is Guid tenantId
            ? await dbContext.UserTenantRoles.Where(item => item.UserId == current.UserId && item.TenantId == tenantId).Select(item => item.Role.Name).Distinct().ToArrayAsync(cancellationToken)
            : [];
        var access = accessTokenService.Create(current.UserId, current.TenantId, current.UserTenantId, roles);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(new RefreshResponse(access.Token, access.ExpiresAtUtc, replacement.Raw));
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUserId();
        if (!passwordPolicy.IsValid(request.NewPassword, out _)) return BadRequest();
        var user = await dbContext.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null || !user.IsActive || !passwordHashService.Verify(request.CurrentPassword, user.PasswordHash)) return Unauthorized();

        user.ChangePassword(passwordHashService.Hash(request.NewPassword));
        var now = DateTime.UtcNow;
        var sessions = await dbContext.RefreshTokens.Where(item => item.UserId == userId && item.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Guid RequireUserId() => Guid.TryParse(User.FindFirstValue("user_id") ?? User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
        ? userId
        : throw new UnauthorizedAccessException("An authenticated user is required.");
}

public sealed record RefreshRequest(string RefreshToken);
public sealed record RefreshResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken);
public sealed record SessionResponse(Guid Id, Guid? TenantId, Guid? UserTenantId, DateTime CreatedAtUtc, DateTime ExpiresAtUtc, DateTime? RevokedAtUtc, bool IsActive);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
