using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/auth/password")]
public sealed class PasswordRecoveryController(
    ShineDbContext dbContext,
    IPasswordHashService passwordHashService,
    IPasswordPolicy passwordPolicy) : ControllerBase
{
    [HttpPost("recovery")]
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
            dbContext.PasswordResetTokens.Add(new PasswordResetToken(user.Id, tokenHash, now.AddMinutes(30)));
            await dbContext.SaveChangesAsync(cancellationToken);
            _ = rawToken; // Entrega será realizada pelo serviço de e-mail do DEV-47.
        }

        return Accepted();
    }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset(PasswordResetRequest request, CancellationToken cancellationToken)
    {
        if (!passwordPolicy.IsValid(request.NewPassword, out _)) return BadRequest();
        var token = await dbContext.PasswordResetTokens.Include(item => item.User)
            .SingleOrDefaultAsync(item => item.TokenHash == PasswordResetTokenService.Hash(request.Token), cancellationToken);
        if (token is null || !token.IsValid(DateTime.UtcNow) || !token.User.IsActive) return BadRequest();

        token.User.ChangePassword(passwordHashService.Hash(request.NewPassword));
        token.MarkUsed(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}
