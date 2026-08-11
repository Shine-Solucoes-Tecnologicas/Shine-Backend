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
    public async Task<ActionResult<AdministrativeDashboardSummary>> Summary(CancellationToken cancellationToken)
        => Ok(new AdministrativeDashboardSummary(
            await db.Tenants.CountAsync(cancellationToken),
            await db.Users.CountAsync(cancellationToken),
            await db.AuditEntries.CountAsync(cancellationToken),
            await db.OperationalLogs.CountAsync(cancellationToken)));
}

public sealed record AdministrativeDashboardSummary(int Tenants, int Users, int AuditEvents, int OperationalLogs);
