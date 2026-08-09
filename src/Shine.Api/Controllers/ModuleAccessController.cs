using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Shine.Infrastructure;
using Shine.Api;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/modules/access")]
public sealed class ModuleAccessController(IModuleAccess moduleAccess, ICurrentTenant currentTenant, ICurrentUser currentUser, IPermissionAuthorization permissions) : ControllerBase
{
    [HttpPut("{moduleCode}")]
    [RequiresPermission("modules.manage")]
    public async Task<IActionResult> Set(string moduleCode, SetModuleAccessRequest request, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid tenantId || currentUser.UserId is not Guid userId) return Unauthorized();
        if (!await permissions.HasPermissionAsync(userId, tenantId, "modules.manage", cancellationToken)) return Forbid();
        await moduleAccess.SetAsync(tenantId, moduleCode, request.Enabled, cancellationToken);
        return NoContent();
    }
}

public sealed record SetModuleAccessRequest(bool Enabled);
