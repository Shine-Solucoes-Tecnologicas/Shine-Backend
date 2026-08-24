using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth/email-verification")]
public sealed class EmailVerificationController(
    ShineDbContext dbContext,
    EmailVerificationChallengeService challengeService) : ControllerBase
{
    [HttpPost("resend")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.EmailVerificationResend)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Resend(EmailVerificationResendRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalizedEmail = Shine.Domain.Identity.User.NormalizeEmail(request.Email);
            var user = await dbContext.Users.SingleOrDefaultAsync(
                candidate => candidate.NormalizedEmail == normalizedEmail, cancellationToken);
            if (user is not null)
                await challengeService.IssueAsync(user, cancellationToken);
        }

        return Accepted();
    }

    [HttpPost("confirm")]
    [EnableRateLimiting(AuthenticationRateLimitPolicies.EmailVerificationConfirm)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Confirm(EmailVerificationConfirmRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token)) return BadRequest();

        var tokenHash = EmailVerificationTokenService.Hash(request.Token);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var token = await dbContext.EmailVerificationTokens.FromSqlInterpolated($$"""
            SELECT * FROM "EmailVerificationTokens" WHERE "TokenHash" = {{tokenHash}} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        var now = DateTime.UtcNow;
        if (token is null || !token.IsValid(now)) return BadRequest();

        var user = await dbContext.Users.SingleOrDefaultAsync(candidate => candidate.Id == token.UserId, cancellationToken);
        if (user is null || !user.IsActive) return BadRequest();

        user.VerifyEmail(now);
        var pending = await dbContext.EmailVerificationTokens
            .Where(candidate => candidate.UserId == user.Id && candidate.UsedAtUtc == null)
            .ToListAsync(cancellationToken);
        foreach (var candidate in pending) candidate.MarkUsed(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record EmailVerificationResendRequest(string Email);
public sealed record EmailVerificationConfirmRequest(string Token);
