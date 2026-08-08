using Microsoft.AspNetCore.Mvc;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/modules/access")]
public sealed class ModuleAccessController(IModuleAccess moduleAccess, ICurrentTenant currentTenant, ICurrentUser currentUser, IPermissionAuthorization permissions) : ControllerBase
{
    [HttpPut("{moduleCode}")]
    public async Task<IActionResult> Set(string moduleCode, SetModuleAccessRequest request, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid tenantId || currentUser.UserId is not Guid userId) return Unauthorized();
        if (!await permissions.HasPermissionAsync(userId, tenantId, "modules.manage", cancellationToken)) return Forbid();
        await moduleAccess.SetAsync(tenantId, moduleCode, request.Enabled, cancellationToken);
        return NoContent();
    }
}

public sealed record SetModuleAccessRequest(bool Enabled);
