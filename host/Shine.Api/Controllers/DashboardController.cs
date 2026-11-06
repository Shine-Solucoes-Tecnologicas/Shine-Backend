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
        return Ok(result);
    }

    [HttpPut("layout")]
    [RequiresPermission("dashboard.manage")]
    [ProducesResponseType<DashboardLayoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<StaleDashboardLayoutProblem>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DashboardLayoutResult>> SaveLayout(DashboardLayoutRequest request, CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || user.UserId is not Guid userId) return Unauthorized();
        try { return Ok(await layouts.SaveAsync(tenantId, userId, request, cancellationToken)); }
        catch (StaleDashboardLayoutException exception)
        {
            return Conflict(new StaleDashboardLayoutProblem(
                "STALE_DASHBOARD_LAYOUT",
                "O layout foi alterado por outra sessão. Recarregue a versão atual antes de salvar novamente.",
                exception.CurrentVersion));
        }
        catch (ArgumentException exception) { return BadRequest(new { code = "INVALID_DASHBOARD_LAYOUT", message = exception.Message }); }
    }

    [HttpPost("layout/reset")]
    [RequiresPermission("dashboard.manage")]
    [ProducesResponseType<DashboardLayoutResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<StaleDashboardLayoutProblem>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<DashboardLayoutResult>> ResetLayout(DashboardLayoutResetRequest request, CancellationToken cancellationToken)
    {
        if (tenant.TenantId is not Guid tenantId || user.UserId is not Guid userId) return Unauthorized();
        try { return Ok(await layouts.ResetAsync(tenantId, userId, request, cancellationToken)); }
        catch (StaleDashboardLayoutException exception)
        {
            return Conflict(new StaleDashboardLayoutProblem("STALE_DASHBOARD_LAYOUT",
                "O layout foi alterado por outra sessão. Recarregue a versão atual antes de tentar novamente.",
                exception.CurrentVersion));
        }
    }
}

public sealed record StaleDashboardLayoutProblem(string Code, string Message, int? CurrentVersion);
