using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Application;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/admin/users")]
[RequiresGlobalPermission("admin.read")]
public sealed class AdministrativeUsersController(ShineDbContext db, Shine.Infrastructure.ICurrentUser currentUser) : ControllerBase
{
    [HttpPut("{userId:guid}/block")]
    [RequiresGlobalPermission("admin.manage")]
    public Task<IActionResult> Block(Guid userId, BlockUserRequest request, CancellationToken cancellationToken) => SetActiveAsync(userId, false, request.Reason, cancellationToken);

    [HttpPut("{userId:guid}/unblock")]
    [RequiresGlobalPermission("admin.manage")]
    public Task<IActionResult> Unblock(Guid userId, CancellationToken cancellationToken) => SetActiveAsync(userId, true, null, cancellationToken);

    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<AdministrativeUserDetailResponse>> Detail(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(item => item.Id == userId)
            .Select(item => new AdministrativeUserDetailResponse(
                item.Id,
                item.Email,
                item.IsActive,
                item.CreatedAtUtc,
                item.BlockReason,
                item.BlockedAtUtc,
                item.BlockedByUserId,
                item.Tenants
                    .Where(link => link.IsActive)
                    .OrderBy(link => link.Tenant.Name)
                    .Select(link => new AdministrativeUserTenantResponse(link.TenantId, link.Tenant.Name))
                    .ToArray(),
                db.UserGlobalRoles
                    .Where(role => role.UserId == item.Id)
                    .OrderBy(role => role.Role.Name)
                    .Select(role => role.Role.Name)
                    .ToArray()))
            .SingleOrDefaultAsync(cancellationToken);

        return user is null ? NotFound() : Ok(user);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdministrativeUserResponse>>> List(
        [FromQuery] PagedRequest request,
        [FromQuery] string? search,
        [FromQuery] bool? active,
        [FromQuery] Guid? tenantId,
        CancellationToken cancellationToken)
    {
        var page = request.ValidatedPage;
        var pageSize = request.ValidatedPageSize;
        var query = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(user => user.Email.ToLower().Contains(normalizedSearch));
        }

        if (active is not null)
            query = query.Where(user => user.IsActive == active.Value);

        if (tenantId is not null)
            query = query.Where(user => user.Tenants.Any(link => link.TenantId == tenantId.Value && link.IsActive));

        var totalItems = await query.CountAsync(cancellationToken);
        var users = await query
            .OrderBy(user => user.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(user => new AdministrativeUserResponse(
                user.Id,
                user.Email,
                user.IsActive,
                user.CreatedAtUtc,
                user.Tenants
                    .Where(link => link.IsActive)
                    .OrderBy(link => link.Tenant.Name)
                    .Select(link => new AdministrativeUserTenantResponse(link.TenantId, link.Tenant.Name))
                    .ToArray()))
            .ToArrayAsync(cancellationToken);

        return Ok(PagedResponse<AdministrativeUserResponse>.Create(users, page, pageSize, totalItems));
    }

    private async Task<IActionResult> SetActiveAsync(Guid userId, bool active, string? reason, CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null) return NotFound();

        if (active) user.Unblock();
        else
        {
            if (currentUser.UserId is not Guid administratorUserId) return Unauthorized();
            if (string.IsNullOrWhiteSpace(reason)) return BadRequest();
            user.Block(reason, administratorUserId, DateTime.UtcNow);
        }

        if (!active)
        {
            var sessions = await db.RefreshTokens
                .Where(token => token.UserId == userId && token.RevokedAtUtc == null)
                .ToListAsync(cancellationToken);
            foreach (var session in sessions) session.Revoke(DateTime.UtcNow);
        }

        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
}

public sealed record AdministrativeUserResponse(
    Guid Id,
    string Email,
    bool IsActive,
    DateTime CreatedAtUtc,
    IReadOnlyCollection<AdministrativeUserTenantResponse> Tenants);

public sealed record AdministrativeUserTenantResponse(Guid Id, string Name);

public sealed record AdministrativeUserDetailResponse(
    Guid Id,
    string Email,
    bool IsActive,
    DateTime CreatedAtUtc,
    string? BlockReason,
    DateTime? BlockedAtUtc,
    Guid? BlockedByUserId,
    IReadOnlyCollection<AdministrativeUserTenantResponse> Tenants,
    IReadOnlyCollection<string> GlobalRoles);

public sealed record BlockUserRequest(string Reason);
