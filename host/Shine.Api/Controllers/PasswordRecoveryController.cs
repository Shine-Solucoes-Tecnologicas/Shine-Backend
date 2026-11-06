using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Microsoft.AspNetCore.RateLimiting;

namespace Shine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth/password")]
public sealed class PasswordRecoveryController(
    ShineDbContext dbContext,
    IPasswordHashService passwordHashService,
    IPasswordPolicy passwordPolicy,
    IPasswordRecoveryMessageTemplate messageTemplate,
    IPasswordRecoveryDelivery delivery,
    ILogger<PasswordRecoveryController> logger,
    IAuthenticationSecurityEventEmitter securityEvents,
    IWebSessionTransport webSession) : ControllerBase
{
    [HttpPost("recovery")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.PasswordRecovery)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> RequestRecovery(PasswordRecoveryRequest request, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users.SingleOrDefaultAsync(
            candidate => candidate.NormalizedEmail == Shine.Domain.Identity.User.NormalizeEmail(request.Email), cancellationToken);
        if (user is not null && user.IsActive)
        {
            var now = DateTime.UtcNow;
            var previous = await dbContext.PasswordResetTokens
                .Where(token => token.UserId == user.Id && token.UsedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var token in previous) token.MarkUsed(now);

            var (rawToken, tokenHash) = PasswordResetTokenService.Create();
            var expiresAt = now.AddMinutes(30);
            dbContext.PasswordResetTokens.Add(new PasswordResetToken(user.Id, tokenHash, expiresAt));
            Enqueue(AuthenticationSecurityEventNames.PasswordResetRequested, AuthenticationSecurityOutcomes.Succeeded,
                AuthenticationSecurityReasons.Unknown, subjectUserId: user.Id, subjectIdentifier: request.Email);
            await dbContext.SaveChangesAsync(cancellationToken);
            var message = messageTemplate.Create(user.Email, rawToken, expiresAt);
            await delivery.DeliverAsync(user.Email, message, cancellationToken);
            logger.LogInformation("Password recovery delivery prepared for user {UserId}; sensitive content omitted.", user.Id);
        }
        else
        {
            Enqueue(AuthenticationSecurityEventNames.PasswordResetRequested, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.InvalidCredentials, subjectIdentifier: request.Email);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return Accepted();
    }

    [HttpPost("reset")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.PasswordRecovery)]
    public async Task<IActionResult> Reset(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        if (!passwordPolicy.IsValid(request.NewPassword, out var passwordErrors))
        {
            Enqueue(AuthenticationSecurityEventNames.PasswordResetCompleted, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.ValidationFailed);
            await dbContext.SaveChangesAsync(cancellationToken);
            return BadRequest(AuthenticationErrors.PasswordPolicy(passwordErrors, "newPassword"));
        }
        var tokenHash = PasswordResetTokenService.Hash(request.Token);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var token = await dbContext.PasswordResetTokens.FromSqlInterpolated($$"""
            SELECT * FROM "PasswordResetTokens" WHERE "TokenHash" = {{tokenHash}} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (token is null || !token.IsValid(DateTime.UtcNow))
        {
            Enqueue(AuthenticationSecurityEventNames.PasswordResetCompleted, AuthenticationSecurityOutcomes.Denied,
                token is not null && token.ExpiresAtUtc <= DateTime.UtcNow
                    ? AuthenticationSecurityReasons.TokenExpired
                    : AuthenticationSecurityReasons.TokenRevoked,
                subjectUserId: token?.UserId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthenticationErrors.Result(400, AuthenticationErrors.PasswordResetInvalid,
                "O link de recuperação é inválido ou expirou.");
        }
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == token.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            Enqueue(AuthenticationSecurityEventNames.PasswordResetCompleted, AuthenticationSecurityOutcomes.Denied,
                AuthenticationSecurityReasons.AccountInactive, subjectUserId: token.UserId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthenticationErrors.Result(400, AuthenticationErrors.PasswordResetInvalid,
                "O link de recuperação é inválido ou expirou.");
        }

        user.ChangePassword(passwordHashService.Hash(request.NewPassword));
        token.MarkUsed(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        var sessions = await dbContext.RefreshTokens
            .Where(item => item.UserId == token.UserId && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);
        Enqueue(AuthenticationSecurityEventNames.PasswordResetCompleted, AuthenticationSecurityOutcomes.Succeeded,
            AuthenticationSecurityReasons.PasswordReset, user.Id, user.Id);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        webSession.ClearSession(HttpContext);
        return NoContent();
    }

    private void Enqueue(
        string eventName,
        string outcome,
        string reason,
        Guid? actorUserId = null,
        Guid? subjectUserId = null,
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
            SubjectIdentifier: subjectIdentifier is { Length: <= 512 } ? subjectIdentifier : null,
            NetworkAddress: context?.Connection.RemoteIpAddress?.ToString(),
            UserAgent: context?.Request.Headers.UserAgent.ToString()));
    }
}
