using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Options;
using Shine.Domain;

namespace Shine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
public sealed class RegistrationController(
    ShineDbContext dbContext,
    IPasswordHashService passwordHashService,
    IPasswordPolicy passwordPolicy,
    IAccessTokenService accessTokenService,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<RegistrationResponse>> Register(RegistrationRequest request, CancellationToken cancellationToken)
    {
        if (!passwordPolicy.IsValid(request.Password, out _)) return BadRequest();
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.TenantName)) return BadRequest();
        var normalizedEmail = Shine.Domain.Identity.User.NormalizeEmail(request.Email);
        if (await dbContext.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail, cancellationToken)) return Conflict();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var user = new User(request.Email, passwordHashService.Hash(request.Password));
        var tenant = new Tenant(request.TenantName);
        dbContext.Users.Add(user);
        dbContext.Tenants.Add(tenant);
        dbContext.UserTenants.Add(new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true));
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuthorizationSeed.SeedTenantDefaultsAsync(dbContext, tenant.Id, user.Id, user.Id, cancellationToken);
        await AuthorizationSeed.EnsureGlobalRolesAsync(dbContext, cancellationToken);
        dbContext.ModuleAccesses.Add(new ModuleAccess(tenant.Id, "CORE"));
        await dbContext.SaveChangesAsync(cancellationToken);
        var roles = await dbContext.UserTenantRoles.Where(link => link.UserId == user.Id && link.TenantId == tenant.Id).Select(link => link.Role.Name).Distinct().ToArrayAsync(cancellationToken);
        var access = accessTokenService.Create(user.Id, tenant.Id, user.Tenants.Single().UserTenantId, roles);
        var refresh = RefreshTokenHash.Create();
        dbContext.RefreshTokens.Add(new RefreshToken(user.Id, tenant.Id, user.Tenants.Single().UserTenantId, refresh.Hash, DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Created("/api/auth/login", new RegistrationResponse(user.Id, tenant.Id, access.Token, access.ExpiresAtUtc, refresh.Raw));
    }
}

public sealed record RegistrationRequest(string Email, string Password, string TenantName);
public sealed record RegistrationResponse(Guid UserId, Guid TenantId, string AccessToken, DateTime AccessTokenExpiresAtUtc, string RefreshToken);
