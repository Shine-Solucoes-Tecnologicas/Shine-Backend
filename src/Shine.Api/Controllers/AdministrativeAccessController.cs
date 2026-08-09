using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Api;
using Shine.Domain.Authorization;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/admin/access")]
[RequiresGlobalPermission("admin.manage")]
public sealed class AdministrativeAccessController(ShineDbContext db) : ControllerBase
{
    [HttpGet("roles")]
    public async Task<ActionResult<IReadOnlyCollection<GlobalRoleResponse>>> Roles(CancellationToken cancellationToken)
    {
        var roles = await db.GlobalRoles
            .AsNoTracking()
            .OrderBy(role => role.Name)
            .Select(role => new GlobalRoleResponse(
                role.Id,
                role.Name,
                role.Permissions.Select(link => link.Permission.Code).OrderBy(code => code).ToArray()))
            .ToArrayAsync(cancellationToken);

        return Ok(roles);
    }

    [HttpGet("users/{userId:guid}/roles")]
    public async Task<ActionResult<IReadOnlyCollection<string>>> UserRoles(Guid userId, CancellationToken cancellationToken)
    {
        var exists = await db.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken);
        if (!exists) return NotFound();

        var roles = await db.UserGlobalRoles
            .AsNoTracking()
            .Where(link => link.UserId == userId)
            .Select(link => link.Role.Name)
            .OrderBy(name => name)
            .ToArrayAsync(cancellationToken);

        return Ok(roles);
    }

    [HttpPut("users/{userId:guid}/roles/{roleName}")]
    public async Task<IActionResult> AssignRole(Guid userId, string roleName, CancellationToken cancellationToken)
    {
        var userExists = await db.Users.AnyAsync(user => user.Id == userId, cancellationToken);
        if (!userExists) return NotFound();

        var role = await db.GlobalRoles.SingleOrDefaultAsync(item => item.Name == roleName, cancellationToken);
        if (role is null) return NotFound();

        var assignmentExists = await db.UserGlobalRoles.AnyAsync(item => item.UserId == userId && item.RoleId == role.Id, cancellationToken);
        if (!assignmentExists)
            db.UserGlobalRoles.Add(new UserGlobalRole(userId, role.Id));

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpDelete("users/{userId:guid}/roles/{roleName}")]
    public async Task<IActionResult> RemoveRole(Guid userId, string roleName, CancellationToken cancellationToken)
    {
        var assignment = await db.UserGlobalRoles
            .SingleOrDefaultAsync(item => item.UserId == userId && item.Role.Name == roleName, cancellationToken);
        if (assignment is null) return NotFound();

        db.UserGlobalRoles.Remove(assignment);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record GlobalRoleResponse(Guid Id, string Name, IReadOnlyCollection<string> Permissions);
