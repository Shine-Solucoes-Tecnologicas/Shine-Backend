using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController(DashboardLayoutService layouts, IDashboardWidgetResolver widgets, ICurrentTenant tenant, ICurrentUser user) : ControllerBase
{
    [HttpGet("widgets")]
    [RequiresPermission("dashboard.read")]
    public async Task<ActionResult<IReadOnlyCollection<AvailableDashboardWidget>>> GetWidgets(CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || user.UserId is not Guid userId) return Unauthorized();
        return Ok(await widgets.ResolveAsync(tenantId, userId, cancellationToken));
    }

    [HttpGet("layout")]
    [RequiresPermission("dashboard.read")]
    public async Task<ActionResult<DashboardLayoutResult>> GetLayout([FromQuery] string dashboardKey = "MAIN", CancellationToken cancellationToken = default)
    {
        if (tenant.TenantId is not Guid tenantId || user.UserId is not Guid userId) return Unauthorized();
        var result = await layouts.GetResolvedAsync(tenantId, userId, dashboardKey, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("layout")]
    [RequiresPermission("dashboard.manage")]
    public async Task<ActionResult<DashboardLayoutResult>> SaveLayout(DashboardLayoutRequest request, CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || user.UserId is not Guid userId) return Unauthorized();
        try { return Ok(await layouts.SaveAsync(tenantId, userId, request, cancellationToken)); }
        catch (ArgumentException exception) { return BadRequest(new { code = "INVALID_DASHBOARD_LAYOUT", message = exception.Message }); }
    }
}
