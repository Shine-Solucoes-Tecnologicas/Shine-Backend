using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Shine.Infrastructure;
using Shine.Api;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/feature-flags")]
public sealed class FeatureFlagsController(IFeatureFlags featureFlags, ICurrentTenant currentTenant, ICurrentUser currentUser, IPermissionAuthorization permissions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyDictionary<string, bool>>> Get(CancellationToken cancellationToken)
        => Ok(await featureFlags.GetAllAsync(currentTenant.TenantId, cancellationToken));

    [HttpGet("catalog")]
    public async Task<ActionResult<IReadOnlyCollection<string>>> Catalog(CancellationToken cancellationToken)
        => Ok(await featureFlags.GetCatalogAsync(cancellationToken));

    [HttpPut("{key}")]
    [RequiresPermission("tenant.manage")]
    public async Task<IActionResult> Set(string key, SetFeatureFlagRequest request, CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid tenantId || currentUser.UserId is not Guid userId) return Unauthorized();
        if (!await permissions.HasPermissionAsync(userId, tenantId, "tenant.manage", cancellationToken)) return Forbid();
        await featureFlags.SetForTenantAsync(tenantId, key, request.Enabled, cancellationToken);
        return NoContent();
    }
}

public sealed record SetFeatureFlagRequest(bool Enabled);
