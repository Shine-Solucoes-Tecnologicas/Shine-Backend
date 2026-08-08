using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/tenants")]
public sealed class TenantsController(
    ShineDbContext dbContext,
    ICurrentUser currentUser,
    IAccessTokenService accessTokenService) : ControllerBase
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
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var tenant = new Tenant(request.Name);
        dbContext.Tenants.Add(tenant);
        dbContext.UserTenants.Add(new UserTenant(userId, tenant.Id, userId, isOwner: true));
        await dbContext.SaveChangesAsync(cancellationToken);
        await AuthorizationSeed.SeedTenantDefaultsAsync(dbContext, tenant.Id, userId, userId, cancellationToken);
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
        return Ok(new TenantSelectionResponse(link.TenantId, link.Tenant.Name, token.Token, token.ExpiresAtUtc));
    }

    private Guid RequireUser() => currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");
}

public sealed record TenantSelectionRequest(Guid TenantId);
public sealed record CreateTenantRequest(string Name);
public sealed record TenantSelectionResponse(Guid TenantId, string TenantName, string AccessToken, DateTime ExpiresAtUtc);
