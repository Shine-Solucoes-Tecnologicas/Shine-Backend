using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController, AllowAnonymous, Route("api/auth/email-verification")]
public sealed class EmailVerificationController(
    ShineDbContext dbContext, EmailVerificationChallengeService challenges,
    IAccessTokenService accessTokens, IOptions<JwtOptions> jwtOptions,
    ITenantExecutionContext tenantExecutionContext, IWebSessionTransport webSession) : ControllerBase
{
    [HttpPost("resend"), EnableRateLimiting(AuthenticationRateLimitPolicies.EmailVerificationResend)]
    public async Task<IActionResult> Resend(EmailVerificationResendRequest request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.Email))
        {
            var normalized = Shine.Domain.Identity.User.NormalizeEmail(request.Email);
            var user = await dbContext.Users.SingleOrDefaultAsync(x => x.NormalizedEmail == normalized, cancellationToken);
            if (user is not null) await challenges.IssueAsync(user, cancellationToken);
        }
        return Accepted();
    }

    [HttpPost("confirm"), EnableRateLimiting(AuthenticationRateLimitPolicies.EmailVerificationConfirm)]
    public async Task<ActionResult<EmailVerificationConfirmationResponse>> Confirm(EmailVerificationConfirmRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
            return AuthenticationErrors.Result(400, AuthenticationErrors.EmailVerificationInvalid, "O link de confirmação é inválido ou expirou.", "token");
        await using var transaction = await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var hash = EmailVerificationTokenService.Hash(request.Token);
        var token = await dbContext.EmailVerificationTokens.FromSqlInterpolated($$"""
            SELECT * FROM "EmailVerificationTokens" WHERE "TokenHash" = {{hash}} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        var now = DateTime.UtcNow;
        if (token is null || !token.IsValid(now))
            return AuthenticationErrors.Result(400, AuthenticationErrors.EmailVerificationInvalid, "O link de confirmação é inválido ou expirou.", "token");
        var user = await dbContext.Users.Include(x => x.Tenants).ThenInclude(x => x.Tenant)
            .SingleOrDefaultAsync(x => x.Id == token.UserId, cancellationToken);
        var membership = user?.Tenants.Where(x => x.IsActive && x.Tenant.IsActive).OrderBy(x => x.CreatedAtUtc).FirstOrDefault();
        if (user is null || !user.IsActive || membership is null)
            return AuthenticationErrors.Result(400, AuthenticationErrors.EmailVerificationInvalid, "O link de confirmação é inválido ou expirou.", "token");
        using var tenantScope = tenantExecutionContext.EnterTenant(membership.TenantId);
        user.VerifyEmail(now);
        var pending = await dbContext.EmailVerificationTokens.Where(x => x.UserId == user.Id && x.UsedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var candidate in pending) candidate.MarkUsed(now);
        var roles = await dbContext.UserTenantRoles.Where(x => x.UserId == user.Id && x.TenantId == membership.TenantId)
            .Select(x => x.Role.Name).Distinct().ToArrayAsync(cancellationToken);
        var refresh = RefreshTokenHash.Create();
        var session = new RefreshToken(user.Id, membership.TenantId, membership.UserTenantId, refresh.Hash,
            now.AddDays(jwtOptions.Value.RefreshTokenDays));
        dbContext.RefreshTokens.Add(session);
        var access = accessTokens.Create(user.Id, session.Id, membership.TenantId, membership.UserTenantId, roles);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        webSession.WriteSession(HttpContext, refresh.Raw, session.ExpiresAtUtc);
        return Ok(new EmailVerificationConfirmationResponse(user.Id, membership.TenantId, membership.Tenant.Name,
            access.Token, access.ExpiresAtUtc, webSession.ResponseRefreshToken(refresh.Raw)));
    }
}

public sealed record EmailVerificationResendRequest(string Email);
public sealed record EmailVerificationConfirmRequest(string Token);
public sealed record EmailVerificationConfirmationResponse(Guid UserId, Guid UnitId, string UnitName,
    string AccessToken, DateTime AccessTokenExpiresAtUtc,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? RefreshToken = null);
