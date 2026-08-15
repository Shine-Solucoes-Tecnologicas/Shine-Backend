using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/account/access")]
public sealed class CustomerAccountAccessController(ShineDbContext db, ICurrentUser currentUser, ICurrentTenant currentTenant, ICustomerAccountAuthorization authorization, Shine.Domain.IModuleCatalog modules) : ControllerBase
{
    [HttpGet("roles")]
    public async Task<IActionResult> Roles(CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken); if (context is null) return Forbid();
        var roles = await db.CustomerAccountRoles.AsNoTracking().Where(x => x.AccountId == context.Value.AccountId).OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name, x.IsSystem, Permissions = x.Permissions.Select(p => p.Permission.Code).OrderBy(code => code).ToArray() }).ToArrayAsync(cancellationToken);
        return Ok(roles);
    }

    [HttpGet("users")]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken); if (context is null) return Forbid();
        var users = await db.CustomerAccountUsers.AsNoTracking().Where(x => x.AccountId == context.Value.AccountId).OrderBy(x => x.User.Email)
            .Select(x => new { x.UserId, x.User.Email, x.IsActive, Roles = x.Roles.Select(role => new { role.RoleId, role.Role.Name, role.AllUnits, role.AllModules, Units = role.Units.Select(unit => unit.UnitId).ToArray(), Modules = role.Modules.Select(module => module.ModuleCode).ToArray() }).ToArray() })
            .ToArrayAsync(cancellationToken);
        return Ok(users);
    }

    [HttpPut("users/{userId:guid}/roles/{roleId:guid}")]
    public async Task<IActionResult> Assign(Guid userId, Guid roleId, AssignCustomerRoleRequest request, CancellationToken cancellationToken)
    {
        var context = await RequireManagementContextAsync(cancellationToken); if (context is null) return Forbid();
        var accountId = context.Value.AccountId;
        if ((!request.AllUnits && request.UnitIds.Count == 0) || (!request.AllModules && request.ModuleCodes.Count == 0)) return BadRequest(new { code = "SCOPE_REQUIRED" });
        if (request.AllUnits && request.UnitIds.Count > 0 || request.AllModules && request.ModuleCodes.Count > 0) return BadRequest(new { code = "AMBIGUOUS_SCOPE" });
        if (!await db.CustomerAccountUsers.AnyAsync(x => x.AccountId == accountId && x.UserId == userId && x.IsActive, cancellationToken)) return NotFound();
        if (await db.UserGlobalRoles.AnyAsync(x => x.UserId == userId, cancellationToken)) return BadRequest(new { code = "PLATFORM_USER_NOT_ALLOWED" });
        if (!await db.CustomerAccountRoles.AnyAsync(x => x.Id == roleId && x.AccountId == accountId, cancellationToken)) return NotFound();
        if (!request.AllUnits && await db.Tenants.CountAsync(x => request.UnitIds.Contains(x.Id) && x.CustomerAccountId == accountId, cancellationToken) != request.UnitIds.Distinct().Count()) return BadRequest(new { code = "UNIT_OUT_OF_SCOPE" });

        var normalizedModules = request.ModuleCodes.Select(x => new Shine.Domain.ModuleCode(x).Value).Distinct().ToArray();
        if (!request.AllModules && normalizedModules.Any(code => modules.Modules.All(module => module.Code.Value != code))) return BadRequest(new { code = "MODULE_NOT_REGISTERED" });

        var existing = await db.CustomerAccountUserRoles.Include(x => x.Units).Include(x => x.Modules).SingleOrDefaultAsync(x => x.AccountId == accountId && x.UserId == userId && x.RoleId == roleId, cancellationToken);
        if (existing is null)
        {
            existing = new CustomerAccountUserRole(accountId, userId, roleId);
            db.CustomerAccountUserRoles.Add(existing);
        }
        existing.ReplaceScope(request.AllUnits, request.AllModules, request.UnitIds, normalizedModules);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<(Guid AccountId, Guid UserId)?> RequireManagementContextAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId || currentTenant.TenantId is not Guid unitId) return null;
        var accountId = await db.Tenants.AsNoTracking().Where(x => x.Id == unitId).Select(x => x.CustomerAccountId).SingleOrDefaultAsync(cancellationToken);
        if (accountId is not Guid value || !await authorization.HasPermissionAsync(userId, value, "account.access.manage", cancellationToken: cancellationToken)) return null;
        return (value, userId);
    }
}

public sealed record AssignCustomerRoleRequest(bool AllUnits, bool AllModules, IReadOnlyCollection<Guid> UnitIds, IReadOnlyCollection<string> ModuleCodes);
