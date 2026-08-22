using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Domain.Authorization;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure.Persistence.Seed;
using Shine.Api;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/tenants/users")]
public sealed class TenantUsersController(ShineDbContext db, ICurrentUser currentUser, ICurrentTenant currentTenant, IPermissionAuthorization permissions) : ControllerBase
{
    [HttpGet]
    [RequiresPermission("users.read")]
    public async Task<ActionResult<IReadOnlyCollection<TenantUserResponse>>> List(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanReadAsync(tenantId, cancellationToken)) return Forbid();
        var users = await db.UserTenants.AsNoTracking().Where(link => link.TenantId == tenantId && link.IsActive)
            .OrderBy(link => link.User.Email).Select(link => new TenantUserResponse(link.UserId, link.User.Email, link.IsOwner, link.UserTenantId)).ToArrayAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPost]
    [RequiresPermission("users.manage")]
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
        var memberRole = await AuthorizationSeed.EnsureMemberRoleAsync(db, tenantId, cancellationToken);
        if (!await db.UserTenantRoles.AnyAsync(item => item.UserId == user.Id && item.TenantId == tenantId, cancellationToken))
            db.UserTenantRoles.Add(new UserTenantRole(user.Id, tenantId, memberRole.Id));
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new TenantUserResponse(user.Id, user.Email, link.IsOwner, link.UserTenantId));
    }

    [HttpDelete("{userId:guid}")]
    [RequiresPermission("users.manage")]
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

    [HttpGet("{userId:guid}")]
    [RequiresPermission("users.read")]
    public async Task<ActionResult<TenantUserDetailsResponse>> Get(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await IsActiveMemberAsync(userId, tenantId, cancellationToken)) return NotFound();
        var result = await db.UserTenants.AsNoTracking()
            .Where(link => link.UserId == userId && link.TenantId == tenantId)
            .Select(link => new TenantUserDetailsResponse(link.UserId, link.User.Email, link.IsOwner, link.IsActive, link.UserTenantId, link.CreatedAtUtc))
            .SingleAsync(cancellationToken);
        return Ok(result);
    }

    [HttpPut("{userId:guid}/status")]
    [RequiresPermission("users.manage")]
    public async Task<ActionResult<TenantUserDetailsResponse>> SetStatus(Guid userId, SetTenantUserStatusRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanManageAsync(tenantId, cancellationToken)) return Forbid();
        var link = await db.UserTenants.SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId, cancellationToken);
        if (link is null) return NotFound();
        if (link.IsOwner && !request.IsActive) return Conflict();
        if (request.IsActive) link.Reactivate(); else link.Deactivate();
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new TenantUserDetailsResponse(link.UserId, (await db.Users.FindAsync([link.UserId], cancellationToken))!.Email, link.IsOwner, link.IsActive, link.UserTenantId, link.CreatedAtUtc));
    }

    [HttpPut("{userId:guid}/metadata")]
    [RequiresPermission("users.manage")]
    public async Task<ActionResult<TenantUserDetailsResponse>> UpdateMetadata(Guid userId, UpdateTenantUserMetadataRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanManageAsync(tenantId, cancellationToken)) return Forbid();
        var link = await db.UserTenants.SingleOrDefaultAsync(item => item.UserId == userId && item.TenantId == tenantId, cancellationToken);
        if (link is null) return NotFound();
        if (request.IsActive != link.IsActive)
        {
            if (link.IsOwner && !request.IsActive) return Conflict();
            if (request.IsActive) link.Reactivate(); else link.Deactivate();
        }
        await db.SaveChangesAsync(cancellationToken);
        var email = await db.Users.Where(user => user.Id == userId).Select(user => user.Email).SingleAsync(cancellationToken);
        return Ok(new TenantUserDetailsResponse(link.UserId, email, link.IsOwner, link.IsActive, link.UserTenantId, link.CreatedAtUtc));
    }

    [HttpGet("{userId:guid}/roles")]
    [RequiresPermission("roles.manage")]
    public async Task<ActionResult<IReadOnlyCollection<TenantRoleResponse>>> Roles(Guid userId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanManageRolesAsync(tenantId, cancellationToken)) return Forbid();
        if (!await IsActiveMemberAsync(userId, tenantId, cancellationToken)) return NotFound();

        var roles = await db.UserTenantRoles.AsNoTracking()
            .Where(link => link.UserId == userId && link.TenantId == tenantId)
            .OrderBy(link => link.Role.Name)
            .Select(link => new TenantRoleResponse(link.RoleId, link.Role.Name, link.Role.IsSystem))
            .ToArrayAsync(cancellationToken);
        return Ok(roles);
    }

    [HttpPut("{userId:guid}/roles")]
    [RequiresPermission("roles.manage")]
    public async Task<ActionResult<IReadOnlyCollection<TenantRoleResponse>>> SetRoles(Guid userId, SetTenantRolesRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await CanManageRolesAsync(tenantId, cancellationToken)) return Forbid();
        var membership = await db.UserTenants.SingleOrDefaultAsync(link => link.UserId == userId && link.TenantId == tenantId && link.IsActive, cancellationToken);
        if (membership is null) return NotFound();
        if (membership.IsOwner) return Conflict();

        var roleIds = request.RoleIds.Distinct().ToArray();
        var roles = await db.Roles.Where(role => role.TenantId == tenantId && roleIds.Contains(role.Id)).ToListAsync(cancellationToken);
        if (roles.Count != roleIds.Length) return BadRequest();
        if (roles.Any(role => role.IsSystem && role.Name == Role.OwnerName)) return BadRequest();

        var current = await db.UserTenantRoles.Where(link => link.UserId == userId && link.TenantId == tenantId).ToListAsync(cancellationToken);
        db.UserTenantRoles.RemoveRange(current.Where(link => !roleIds.Contains(link.RoleId)));
        var currentIds = current.Select(link => link.RoleId).ToHashSet();
        foreach (var roleId in roleIds.Where(roleId => !currentIds.Contains(roleId)))
            db.UserTenantRoles.Add(new UserTenantRole(userId, tenantId, roleId));
        await db.SaveChangesAsync(cancellationToken);

        return Ok(roles.OrderBy(role => role.Name).Select(role => new TenantRoleResponse(role.Id, role.Name, role.IsSystem)).ToArray());
    }

    private Guid RequireTenant() => currentTenant.TenantId ?? throw new UnauthorizedAccessException("An active tenant is required.");
    private Task<bool> CanReadAsync(Guid tenantId, CancellationToken cancellationToken) => currentUser.UserId is Guid userId && currentTenant.HasCompleteContext
        ? permissions.HasPermissionAsync(userId, tenantId, "users.read", cancellationToken) : Task.FromResult(false);
    private Task<bool> CanManageAsync(Guid tenantId, CancellationToken cancellationToken) => currentUser.UserId is Guid userId && currentTenant.HasCompleteContext
        ? permissions.HasPermissionAsync(userId, tenantId, "users.manage", cancellationToken) : Task.FromResult(false);
    private Task<bool> CanManageRolesAsync(Guid tenantId, CancellationToken cancellationToken) => currentUser.UserId is Guid userId && currentTenant.HasCompleteContext
        ? permissions.HasPermissionAsync(userId, tenantId, "roles.manage", cancellationToken) : Task.FromResult(false);
    private Task<bool> IsActiveMemberAsync(Guid userId, Guid tenantId, CancellationToken cancellationToken) =>
        db.UserTenants.AnyAsync(link => link.UserId == userId && link.TenantId == tenantId && link.IsActive, cancellationToken);
}

public sealed record AddTenantUserRequest(string Email);
public sealed record TenantUserResponse(Guid UserId, string Email, bool IsOwner, Guid UserTenantId);
public sealed record SetTenantRolesRequest(IReadOnlyCollection<Guid> RoleIds);
public sealed record TenantRoleResponse(Guid RoleId, string Name, bool IsSystem);
public sealed record SetTenantUserStatusRequest(bool IsActive);
public sealed record TenantUserDetailsResponse(Guid UserId, string Email, bool IsOwner, bool IsActive, Guid UserTenantId, DateTime CreatedAtUtc);
public sealed record UpdateTenantUserMetadataRequest(bool IsActive);
