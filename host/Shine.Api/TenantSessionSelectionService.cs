using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api;

public enum TenantSessionSelectionOutcome
{
    Succeeded,
    Unauthorized,
    Forbidden
}

public sealed record TenantSessionSelectionResult(
    TenantSessionSelectionOutcome Outcome,
    Guid? TenantId = null,
    string? TenantName = null,
    string? AccessToken = null,
    DateTime? AccessTokenExpiresAtUtc = null,
    string? RefreshToken = null,
    DateTime? RefreshTokenExpiresAtUtc = null);

public sealed class TenantSessionSelectionService(
    ShineDbContext dbContext,
    IAccessTokenService accessTokenService,
    IOptions<JwtOptions> jwtOptions,
    IAuthenticationSecurityEventEmitter securityEvents,
    IHttpContextAccessor httpContextAccessor)
{
    public async Task<TenantSessionSelectionResult> SelectAsync(
        Guid userId,
        Guid tenantId,
        string rawRefreshToken,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var tokenHash = RefreshTokenHash.Hash(rawRefreshToken);
        var current = await dbContext.RefreshTokens.FromSqlInterpolated($$"""
            SELECT * FROM "RefreshTokens" WHERE "TokenHash" = {{tokenHash}} FOR UPDATE
            """).SingleOrDefaultAsync(cancellationToken);
        var verifiedUser = current is not null && current.UserId == userId &&
            await dbContext.Users.AnyAsync(user => user.Id == userId && user.IsActive && user.EmailVerifiedAtUtc != null, cancellationToken);
        if (current is null || current.UserId != userId || !current.IsActive(DateTime.UtcNow) || !verifiedUser)
        {
            Enqueue(AuthenticationSecurityOutcomes.Denied, AuthenticationSecurityReasons.TokenRevoked, userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TenantSessionSelectionResult(TenantSessionSelectionOutcome.Unauthorized);
        }

        var membership = await dbContext.UserTenants
            .AsNoTracking()
            .Include(item => item.Tenant)
            .SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId &&
                item.IsActive && item.Tenant.IsActive, cancellationToken);
        if (membership is null)
        {
            Enqueue(AuthenticationSecurityOutcomes.Denied, AuthenticationSecurityReasons.TenantNotAllowed, userId);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new TenantSessionSelectionResult(TenantSessionSelectionOutcome.Forbidden);
        }

        var roles = await dbContext.UserTenantRoles
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.TenantId == tenantId)
            .Select(item => item.Role.Name)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var replacement = RefreshTokenHash.Create();
        current.Revoke(DateTime.UtcNow, replacement.Hash);
        var next = new RefreshToken(userId, tenantId, membership.UserTenantId, replacement.Hash,
            DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays));
        dbContext.RefreshTokens.Add(next);
        var access = accessTokenService.Create(userId, next.Id, tenantId, membership.UserTenantId, roles);
        Enqueue(AuthenticationSecurityOutcomes.Succeeded, AuthenticationSecurityReasons.Unknown,
            userId, next.Id, tenantId);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new TenantSessionSelectionResult(
            TenantSessionSelectionOutcome.Succeeded,
            tenantId,
            membership.Tenant.Name,
            access.Token,
            access.ExpiresAtUtc,
            replacement.Raw,
            next.ExpiresAtUtc);
    }

    private void Enqueue(string outcome, string reason, Guid userId, Guid? sessionId = null, Guid? tenantId = null)
    {
        var context = httpContextAccessor.HttpContext;
        securityEvents.Enqueue(new AuthenticationSecurityEventInput(
            AuthenticationSecurityEventNames.TenantSelection,
            outcome,
            reason,
            context?.TraceIdentifier ?? $"tenant-selection-{Guid.NewGuid():N}",
            userId,
            userId,
            sessionId,
            tenantId,
            NetworkAddress: context?.Connection.RemoteIpAddress?.ToString(),
            UserAgent: context?.Request.Headers.UserAgent.ToString()));
    }
}
