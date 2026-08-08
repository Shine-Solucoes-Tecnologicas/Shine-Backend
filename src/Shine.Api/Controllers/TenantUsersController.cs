using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Domain.Authorization;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/tenants/users")]
public sealed class TenantUsersController(ShineDbContext db, ICurrentUser currentUser, ICurrentTenant currentTenant, IPermissionAuthorization permissions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<TenantUserResponse>>> List(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanReadAsync(tenantId, cancellationToken)) return Forbid();
        var users = await db.UserTenants.AsNoTracking().Where(link => link.TenantId == tenantId && link.IsActive)
            .OrderBy(link => link.User.Email).Select(link => new TenantUserResponse(link.UserId, link.User.Email, link.IsOwner, link.UserTenantId)).ToArrayAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost]
    public async Task<ActionResult<TenantUserResponse>> Add(AddTenantUserRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanManageAsync(tenantId, cancellationToken)) return Forbid();
        var user = await db.Users.SingleOrDefaultAsync(item => item.NormalizedEmail == Shine.Domain.Identity.User.NormalizeEmail(request.Email) && item.IsActive, cancellationToken);
        if (user is null) return NotFound();
        var link = await db.UserTenants.SingleOrDefaultAsync(item => item.UserId == user.Id && item.TenantId == tenantId, cancellationToken);
        if (link is not null)
        {
            if (!link.IsActive) link.Reactivate();
        }
        else
        {
            link = new UserTenant(user.Id, tenantId, currentUser.UserId!.Value, isOwner: false);
            db.UserTenants.Add(link);
        }
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new TenantUserResponse(user.Id, user.Email, link.IsOwner, link.UserTenantId));
    }

    [HttpDelete("{userId:guid}")]
    public async Task<IActionResult> Remove(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanManageAsync(tenantId, cancellationToken)) return Forbid();
        var link = await db.UserTenants.SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId && item.IsActive, cancellationToken);
        if (link is null) return NotFound();
        if (link.IsOwner || await db.UserTenantRoles.AnyAsync(item => item.UserId == userId && item.TenantId == tenantId && item.Role.Name == Role.AdministratorName, cancellationToken))
        {
            var remainingAdmins = await db.UserTenants.CountAsync(item => item.TenantId == tenantId && item.IsActive && (item.IsOwner || db.UserTenantRoles.Any(role => role.UserId == item.UserId && role.TenantId == tenantId && role.Role.Name == Role.AdministratorName)), cancellationToken);
            if (remainingAdmins <= 1) return Conflict();
        }
        link.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Guid RequireTenant() => currentTenant.TenantId ?? throw new UnauthorizedAccessException("An active tenant is required.");
    private Task<bool> CanReadAsync(Guid tenantId, CancellationToken cancellationToken) => currentUser.UserId is Guid userId && currentTenant.HasCompleteContext
        ? permissions.HasPermissionAsync(userId, tenantId, "users.read", cancellationToken) : Task.FromResult(false);
    private Task<bool> CanManageAsync(Guid tenantId, CancellationToken cancellationToken) => currentUser.UserId is Guid userId && currentTenant.HasCompleteContext
        ? permissions.HasPermissionAsync(userId, tenantId, "users.manage", cancellationToken) : Task.FromResult(false);
}

public sealed record AddTenantUserRequest(string Email);
public sealed record TenantUserResponse(Guid UserId, string Email, bool IsOwner, Guid UserTenantId);
