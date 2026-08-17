using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence.Seed;
using Microsoft.Extensions.Options;
using Shine.Domain;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants")]
public sealed class TenantsController(
    ShineDbContext dbContext,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IAccessTokenService accessTokenService,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    [HttpGet("accessible")]
    public async Task<ActionResult<IReadOnlyCollection<AccessibleTenantResponse>>> Accessible(CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenants = await dbContext.UserTenants
            .AsNoTracking()
            .Where(link => link.UserId == userId && link.IsActive && link.Tenant.IsActive)
            .OrderBy(link => link.Tenant.Name)
            .Select(link => new AccessibleTenantResponse(link.TenantId, link.Tenant.Name))
            .ToArrayAsync(cancellationToken);
        return Ok(tenants);
    }

    [HttpPost]
    public async Task<ActionResult<TenantSelectionResponse>> Create(CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantName = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(tenantName) || tenantName.Length > 200) return BadRequest();
        if (await dbContext.Tenants.AnyAsync(tenant => tenant.Name.ToLower() == tenantName.ToLower(), cancellationToken))
            return Conflict();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var accountId = currentTenant.TenantId is Guid selectedTenantId
            ? await dbContext.Tenants.AsNoTracking().Where(x => x.Id == selectedTenantId).Select(x => x.CustomerAccountId).SingleOrDefaultAsync(cancellationToken)
            : null;
        CustomerAccount? newAccount = null;
        if (accountId is null)
        {
            newAccount = new CustomerAccount(tenantName);
            accountId = newAccount.Id;
            dbContext.CustomerAccounts.Add(newAccount);
            dbContext.CustomerAccountUsers.Add(new CustomerAccountUser(accountId.Value, userId));
        }
        var tenant = new Tenant(tenantName);
        tenant.AssignToCustomerAccount(accountId.Value);
        dbContext.Tenants.Add(tenant);
        dbContext.UserTenants.Add(new UserTenant(userId, tenant.Id, userId, isOwner: true));
        await dbContext.SaveChangesAsync(cancellationToken);
        if (newAccount is not null) await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(dbContext, newAccount.Id, userId, cancellationToken);
        await AuthorizationSeed.SeedTenantDefaultsAsync(dbContext, tenant.Id, userId, userId, cancellationToken);
        await AuthorizationSeed.EnsureGlobalRolesAsync(dbContext, cancellationToken);
        var defaultPlan = await dbContext.Plans.Include(x => x.Modules).SingleOrDefaultAsync(x => x.Code == "DEFAULT", cancellationToken);
        if (defaultPlan is null)
        {
            defaultPlan = new Plan("DEFAULT", "Plano padrão");
            defaultPlan.Modules.Add(new PlanModule(defaultPlan.Id, "SCHEDULING"));
            dbContext.Plans.Add(defaultPlan);
        }
        dbContext.TenantPlans.Add(new TenantPlan(tenant.Id, defaultPlan.Id));
        await dbContext.SaveChangesAsync(cancellationToken);
        var roles = await dbContext.UserTenantRoles.Where(item => item.UserId == userId && item.TenantId == tenant.Id).Select(item => item.Role.Name).Distinct().ToArrayAsync(cancellationToken);
        var token = accessTokenService.Create(userId, tenant.Id, tenant.Users.Single().UserTenantId, roles);
        await transaction.CommitAsync(cancellationToken);
        return Created($"/api/tenants/{tenant.Id}", new TenantSelectionResponse(tenant.Id, tenant.Name, token.Token, token.ExpiresAtUtc));
    }

    [HttpPost("select")]
    [HttpPost("switch")]
    public async Task<ActionResult<TenantSelectionResponse>> Select(TenantSelectionRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var link = await dbContext.UserTenants
            .AsNoTracking()
            .Include(item => item.Tenant)
            .SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == request.TenantId && item.IsActive && item.Tenant.IsActive, cancellationToken);
        if (link is null) return Forbid();

        var roles = await dbContext.UserTenantRoles
            .AsNoTracking()
            .Where(item => item.UserId == userId && item.TenantId == link.TenantId)
            .Select(item => item.Role.Name)
            .Distinct()
            .ToArrayAsync(cancellationToken);
        var token = accessTokenService.Create(userId, link.TenantId, link.UserTenantId, roles);
        string? replacementRaw = null;
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            var current = await dbContext.RefreshTokens.SingleOrDefaultAsync(item => item.TokenHash == RefreshTokenHash.Hash(request.RefreshToken) && item.UserId == userId, cancellationToken);
            if (current is not null && current.IsActive(DateTime.UtcNow))
            {
                var replacement = RefreshTokenHash.Create();
                current.Revoke(DateTime.UtcNow, replacement.Hash);
                dbContext.RefreshTokens.Add(new RefreshToken(userId, link.TenantId, link.UserTenantId, replacement.Hash, DateTime.UtcNow.AddDays(jwtOptions.Value.RefreshTokenDays)));
                await dbContext.SaveChangesAsync(cancellationToken);
                replacementRaw = replacement.Raw;
            }
        }
        return Ok(new TenantSelectionResponse(link.TenantId, link.Tenant.Name, token.Token, token.ExpiresAtUtc, replacementRaw));
    }

    private Guid RequireUser() => currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");
}

public sealed record TenantSelectionRequest(Guid TenantId, string? RefreshToken = null);
public sealed record CreateTenantRequest(string Name);
public sealed record TenantSelectionResponse(Guid TenantId, string TenantName, string AccessToken, DateTime ExpiresAtUtc, string? RefreshToken = null);
