using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shine.Api;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/plans")]
public sealed class PlansController(IPlanAccess plans, ICurrentTenant currentTenant, ICurrentUser currentUser, IPermissionAuthorization permissions) : ControllerBase
{
    [HttpGet("access/{moduleCode}")]
    public async Task<ActionResult<ModuleAccessResponse>> Access(string moduleCode, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid tenantId) return Unauthorized();
        return Ok(new ModuleAccessResponse(moduleCode, await plans.HasAccessAsync(tenantId, moduleCode, cancellationToken)));
    }

    [HttpPut("plan")]
    [RequiresPermission("tenant.manage")]
    public async Task<IActionResult> SetPlan(SetPlanRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync("tenant.manage", cancellationToken)) return Forbid();
        await plans.SetPlanAsync(currentTenant.TenantId!.Value, request.PlanId, cancellationToken);
        return NoContent();
    }

    [HttpPut("override/{moduleCode}")]
    [RequiresPermission("modules.manage")]
    public async Task<IActionResult> SetOverride(string moduleCode, SetModuleOverrideRequest request, CancellationToken cancellationToken)
    {
        if (!await CanManageAsync("modules.manage", cancellationToken)) return Forbid();
        await plans.SetOverrideAsync(currentTenant.TenantId!.Value, moduleCode, request.Enabled, cancellationToken);
        return NoContent();
    }

    private Task<bool> CanManageAsync(string permission, CancellationToken cancellationToken) => currentTenant.TenantId is Guid tenantId && currentUser.UserId is Guid userId
        ? permissions.HasPermissionAsync(userId, tenantId, permission, cancellationToken)
        : Task.FromResult(false);
}

public sealed record SetPlanRequest(Guid PlanId);
public sealed record SetModuleOverrideRequest(bool Enabled);
public sealed record ModuleAccessResponse(string ModuleCode, bool Enabled);
