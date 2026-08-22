using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public sealed class DashboardController(DashboardLayoutService layouts, IDashboardWidgetResolver widgets,
    IDashboardWidgetCatalog catalog, ICurrentTenant tenant, ICurrentUser user) : ControllerBase
{
    [HttpGet("widgets")]
    [RequiresPermission("dashboard.read")]
    public async Task<ActionResult<IReadOnlyCollection<AvailableDashboardWidget>>> GetWidgets(CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || user.UserId is not Guid userId) return Unauthorized();
        return Ok(await widgets.ResolveAsync(tenantId, userId, cancellationToken));
    }

    [HttpGet("widgets/{widgetKey}/data")]
    [RequiresPermission("dashboard.read")]
    public async Task<ActionResult<DashboardWidgetData>> GetWidgetData(string widgetKey,
        [FromQuery] DateTimeOffset fromUtc, [FromQuery] DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || user.UserId is not Guid userId) return Unauthorized();

        var available = await widgets.ResolveAsync(tenantId, userId, cancellationToken);
        if (!available.Any(x => string.Equals(x.Descriptor.WidgetKey, widgetKey, StringComparison.OrdinalIgnoreCase)))
            return NotFound();

        try
        {
            var context = new DashboardWidgetContext(tenantId, userId, fromUtc, toUtc);
            return Ok(await catalog.Get(widgetKey).GetDataAsync(context, cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { code = "INVALID_WIDGET_PERIOD", message = exception.Message });
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
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
