using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Application;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/admin/operational-logs")]
[RequiresGlobalPermission("admin.read")]
public sealed class AdministrativeOperationalLogsController(ShineDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<OperationalLogResponse>>> List(
        [FromQuery] PagedRequest page,
        [FromQuery] string? level,
        [FromQuery] string? category,
        [FromQuery] string? correlationId,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        CancellationToken cancellationToken)
    {
        var query = db.OperationalLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(level)) query = query.Where(item => item.Level == level);
        if (!string.IsNullOrWhiteSpace(category)) query = query.Where(item => item.Category == category);
        if (!string.IsNullOrWhiteSpace(correlationId)) query = query.Where(item => item.CorrelationId == correlationId);
        if (fromUtc is not null) query = query.Where(item => item.CreatedAtUtc >= fromUtc.Value);
        if (toUtc is not null) query = query.Where(item => item.CreatedAtUtc <= toUtc.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(item => item.CreatedAtUtc)
            .Skip((page.ValidatedPage - 1) * page.ValidatedPageSize).Take(page.ValidatedPageSize)
            .Select(item => new OperationalLogResponse(item.Id, item.CreatedAtUtc, item.Level, item.Category, item.Message, item.Exception, item.CorrelationId, item.TraceId))
            .ToArrayAsync(cancellationToken);
        return Ok(PagedResponse<OperationalLogResponse>.Create(items, page.ValidatedPage, page.ValidatedPageSize, total));
    }
}

public sealed record OperationalLogResponse(Guid Id, DateTime CreatedAtUtc, string Level, string Category, string Message, string? Exception, string? CorrelationId, string? TraceId);
