using Microsoft.AspNetCore.Mvc;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/feature-flags")]
public sealed class FeatureFlagsController(IFeatureFlags featureFlags, ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyDictionary<string, bool>>> Get(CancellationToken cancellationToken)
        => Ok(await featureFlags.GetAllAsync(currentTenant.TenantId, cancellationToken));
}
