using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[RequiresGlobalPermission("admin.read")]
public sealed class AdministrativeDashboardController(ShineDbContext db) : ControllerBase
{
    [HttpGet("summary")]
    public async Task<ActionResult<AdministrativeDashboardSummary>> Summary(
        [FromQuery] AdministrativeDashboardRequest request,
        CancellationToken cancellationToken)
    {
        var toUtc = request.ToUtc?.ToUniversalTime() ?? DateTime.UtcNow;
        var fromUtc = request.FromUtc?.ToUniversalTime() ?? toUtc.AddHours(-24);
        if (fromUtc >= toUtc)
            return BadRequest(new AdministrativeDashboardError("dashboard_period_invalid", "O início do período deve ser anterior ao fim."));
        if (toUtc - fromUtc > TimeSpan.FromDays(90))
            return BadRequest(new AdministrativeDashboardError("dashboard_period_too_large", "O período máximo para indicadores é de 90 dias."));

        var tenants = await db.Tenants.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new AdministrativeTenantIndicators(
                group.Count(),
                group.Count(tenant => tenant.IsActive),
                group.Count(tenant => !tenant.IsActive)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new AdministrativeTenantIndicators(0, 0, 0);

        var users = await db.Users.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(group => new AdministrativeUserIndicators(
                group.Count(),
                group.Count(user => user.IsActive),
                group.Count(user => !user.IsActive)))
            .SingleOrDefaultAsync(cancellationToken)
            ?? new AdministrativeUserIndicators(0, 0, 0);

        var administrativeEvents = db.AuditEntries.AsNoTracking()
            .Where(entry => entry.TenantId == null && entry.OccurredAtUtc >= fromUtc && entry.OccurredAtUtc < toUtc);
        var auditEventCount = await administrativeEvents.CountAsync(cancellationToken);
        var recentEvents = await administrativeEvents
            .OrderByDescending(entry => entry.OccurredAtUtc)
            .ThenByDescending(entry => entry.Id)
            .Take(10)
            .Select(entry => new AdministrativeDashboardEvent(
                entry.Id,
                entry.EntityType,
                entry.Action,
                entry.UserId,
                entry.OccurredAtUtc,
                entry.IsSystemOperation))
            .ToArrayAsync(cancellationToken);

        var operationalLogCount = await db.OperationalLogs.AsNoTracking()
            .CountAsync(entry => entry.CreatedAtUtc >= fromUtc && entry.CreatedAtUtc < toUtc, cancellationToken);

        return Ok(new AdministrativeDashboardSummary(
            DateTime.UtcNow,
            new AdministrativeDashboardPeriod(fromUtc, toUtc),
            tenants,
            users,
            auditEventCount,
            operationalLogCount,
            recentEvents));
    }
}

public sealed record AdministrativeDashboardRequest(DateTime? FromUtc = null, DateTime? ToUtc = null);

public sealed record AdministrativeDashboardSummary(
    DateTime GeneratedAtUtc,
    AdministrativeDashboardPeriod Period,
    AdministrativeTenantIndicators Tenants,
    AdministrativeUserIndicators Users,
    int AuditEvents,
    int OperationalLogs,
    IReadOnlyCollection<AdministrativeDashboardEvent> RecentEvents);

public sealed record AdministrativeDashboardPeriod(DateTime FromUtc, DateTime ToUtc);
public sealed record AdministrativeTenantIndicators(int Total, int Active, int Suspended);
public sealed record AdministrativeUserIndicators(int Total, int Active, int Blocked);
public sealed record AdministrativeDashboardEvent(
    Guid Id,
    string EntityType,
    string Action,
    Guid? UserId,
    DateTime OccurredAtUtc,
    bool IsSystemOperation);
public sealed record AdministrativeDashboardError(string Code, string Message);
