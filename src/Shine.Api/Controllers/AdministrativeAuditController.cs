using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Application;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/admin/audit")]
[RequiresGlobalPermission("admin.audit")]
public sealed class AdministrativeAuditController(ShineDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AdministrativeAuditResponse>>> List(
        [FromQuery] PagedRequest request,
        [FromQuery] string? entityType,
        [FromQuery] string? action,
        [FromQuery] Guid? userId,
        [FromQuery] Guid? tenantId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var page = request.ValidatedPage;
        var pageSize = request.ValidatedPageSize;
        var query = db.AuditEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(entry => entry.EntityType == entityType.Trim());
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(entry => entry.Action == action.Trim());
        if (userId is not null) query = query.Where(entry => entry.UserId == userId.Value);
        if (tenantId is not null) query = query.Where(entry => entry.TenantId == tenantId.Value);
        if (fromUtc is not null) query = query.Where(entry => entry.OccurredAtUtc >= fromUtc.Value.ToUniversalTime());
        if (toUtc is not null) query = query.Where(entry => entry.OccurredAtUtc <= toUtc.Value.ToUniversalTime());

        var totalItems = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(entry => new AdministrativeAuditResponse(
                entry.Id,
                entry.EntityType,
                entry.EntityId,
                entry.Action,
                entry.UserId,
                entry.TenantId,
                entry.OccurredAtUtc,
                entry.CorrelationId,
                entry.OldValuesJson,
                entry.NewValuesJson))
            .ToArrayAsync(cancellationToken);

        return Ok(PagedResponse<AdministrativeAuditResponse>.Create(items, page, pageSize, totalItems));
    }
}

public sealed record AdministrativeAuditResponse(
    Guid Id,
    string EntityType,
    string EntityId,
    string Action,
    Guid? UserId,
    Guid? TenantId,
    DateTime OccurredAtUtc,
    string? CorrelationId,
    string? OldValuesJson,
    string? NewValuesJson);
