using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shine.Api;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/settings")]
public sealed class FunctionalSettingsController(IFunctionalSettings settings, ICurrentTenant currentTenant, ICurrentUser currentUser, IPermissionAuthorization permissions) : ControllerBase
{
    [HttpGet("{key}")]
    public async Task<ActionResult<FunctionalSettingResponse>> Get(string key, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid tenantId) return Unauthorized();
        var value = await settings.GetAsync(key, tenantId, cancellationToken);
        return value is null ? NotFound() : Ok(new FunctionalSettingResponse(key, value));
    }

    [HttpPut("{key}")]
    [RequiresPermission("tenant.manage")]
    public async Task<IActionResult> Set(string key, SetFunctionalSettingRequest request, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid tenantId || currentUser.UserId is not Guid userId) return Unauthorized();
        if (!await permissions.HasPermissionAsync(userId, tenantId, "tenant.manage", cancellationToken)) return Forbid();
        await settings.SetForTenantAsync(tenantId, key, request.Value, cancellationToken);
        return NoContent();
    }
}

public sealed record SetFunctionalSettingRequest(string Value);
public sealed record FunctionalSettingResponse(string Key, string Value);
