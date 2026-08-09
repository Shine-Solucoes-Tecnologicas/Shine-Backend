using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/modules")]
public sealed class ModulesController(IModuleAccess moduleAccess, ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ModuleDescriptor>>> GetModules(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid tenantId) return Unauthorized();
        return Ok(await moduleAccess.GetAccessibleAsync(tenantId, cancellationToken));
    }
}
