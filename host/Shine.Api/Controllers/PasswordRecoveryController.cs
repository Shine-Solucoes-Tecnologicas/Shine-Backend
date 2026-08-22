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
    ILogger<PasswordRecoveryController> logger) : ControllerBase
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
            await dbContext.SaveChangesAsync(cancellationToken);
            var message = messageTemplate.Create(user.Email, rawToken, expiresAt);
            await delivery.DeliverAsync(user.Email, message, cancellationToken);
            logger.LogInformation("Password recovery delivery prepared for {Email}; sensitive content omitted.", user.Email);
        }

        return Accepted();
    }

    [HttpPost("reset")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.PasswordRecovery)]
    public async Task<IActionResult> Reset(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        if (!passwordPolicy.IsValid(request.NewPassword, out _)) return BadRequest();
        var tokenHash = PasswordResetTokenService.Hash(request.Token);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var token = await dbContext.PasswordResetTokens.FromSqlInterpolated($$"""
            SELECT * FROM "PasswordResetTokens" WHERE "TokenHash" = {{tokenHash}} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        if (token is null || !token.IsValid(DateTime.UtcNow)) return BadRequest();
        var user = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == token.UserId, cancellationToken);
        if (user is null || !user.IsActive) return BadRequest();

        user.ChangePassword(passwordHashService.Hash(request.NewPassword));
        token.MarkUsed(DateTime.UtcNow);
        var now = DateTime.UtcNow;
        var sessions = await dbContext.RefreshTokens
            .Where(item => item.UserId == token.UserId && item.RevokedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }
}
