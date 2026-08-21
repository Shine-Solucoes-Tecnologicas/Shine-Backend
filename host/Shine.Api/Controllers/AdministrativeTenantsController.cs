using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Application;
using Shine.Infrastructure.Persistence;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/admin/tenants")]
[RequiresGlobalPermission("admin.read")]
public sealed class AdministrativeTenantsController(ShineDbContext db, ICurrentUser currentUser) : ControllerBase
{
    [HttpPut("{tenantId:guid}/suspend")]
    [RequiresGlobalPermission("admin.manage")]
    public async Task<IActionResult> Suspend(Guid tenantId, SuspendTenantRequest request, CancellationToken cancellationToken)
    {
        var administratorUserId = currentUser.UserId;
        if (administratorUserId is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(request.Reason)) return BadRequest();

        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return NotFound();

        tenant.Suspend(request.Reason, administratorUserId.Value, DateTime.UtcNow);
        var userIds = await db.UserTenants.Where(link => link.TenantId == tenantId).Select(link => link.UserId).ToArrayAsync(cancellationToken);
        var sessions = await db.RefreshTokens.Where(token => userIds.Contains(token.UserId) &&
            token.TenantId == tenantId && token.RevokedAtUtc == null).ToListAsync(cancellationToken);
        foreach (var session in sessions) session.Revoke(DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPut("{tenantId:guid}/reactivate")]
    [RequiresGlobalPermission("admin.manage")]
    public async Task<IActionResult> Reactivate(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants.SingleOrDefaultAsync(item => item.Id == tenantId, cancellationToken);
        if (tenant is null) return NotFound();
        tenant.Reactivate();
        await db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }
    [HttpGet("{tenantId:guid}")]
    public async Task<ActionResult<AdministrativeTenantResponse>> Detail(Guid tenantId, CancellationToken cancellationToken)
    {
        var tenant = await db.Tenants
            .AsNoTracking()
            .Where(item => item.Id == tenantId)
            .Select(item => new AdministrativeTenantResponse(
                item.Id,
                item.Name,
                item.IsActive,
                item.CreatedAtUtc,
                item.Users.Count(user => user.IsActive)))
            .SingleOrDefaultAsync(cancellationToken);

        return tenant is null ? NotFound() : Ok(tenant);
    }

    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdministrativeTenantResponse>>> List(
        [FromQuery] PagedRequest request,
        [FromQuery] string? search,
        [FromQuery] bool? active,
        CancellationToken cancellationToken)
    {
        var page = request.ValidatedPage;
        var pageSize = request.ValidatedPageSize;
        var query = db.Tenants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(tenant => tenant.Name.ToLower().Contains(normalizedSearch));
        }

        if (active is not null)
            query = query.Where(tenant => tenant.IsActive == active.Value);

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderBy(tenant => tenant.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(tenant => new AdministrativeTenantResponse(
                tenant.Id,
                tenant.Name,
                tenant.IsActive,
                tenant.CreatedAtUtc,
                tenant.Users.Count(user => user.IsActive)))
            .ToArrayAsync(cancellationToken);

        return Ok(PagedResponse<AdministrativeTenantResponse>.Create(items, page, pageSize, totalItems));
    }
}

public sealed record AdministrativeTenantResponse(
    Guid Id,
    string Name,
    bool IsActive,
    DateTime CreatedAtUtc,
    int ActiveUserCount);

public sealed record SuspendTenantRequest(string Reason);
