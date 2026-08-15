using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/admin/plans")]
[RequiresGlobalPermission("admin.read")]
public sealed class AdministrativePlansController(ShineDbContext db, IModuleCatalog modules) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<PlatformPlanResponse>>> Get(CancellationToken cancellationToken)
    {
        var plans = await db.Plans.AsNoTracking()
            .Include(x => x.Modules)
            .OrderBy(x => x.Code)
            .Select(x => new PlatformPlanResponse(x.Id, x.Code, x.Name,
                x.Modules.Select(module => module.ModuleCode).ToArray(),
                db.PlanEntitlements.Where(item => item.PlanId == x.Id)
                    .Select(item => new PlatformEntitlementResponse(item.Key, item.Value)).ToArray()))
            .ToArrayAsync(cancellationToken);
        return Ok(plans);
    }

    [HttpPost]
    [RequiresGlobalPermission("admin.manage")]
    public async Task<ActionResult<PlatformPlanResponse>> Create(CreatePlatformPlanRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name)) return BadRequest();
        var code = Plan.Normalize(request.Code);
        if (await db.Plans.AnyAsync(x => x.Code == code, cancellationToken)) return Conflict();

        var selectedModules = request.Modules.Select(module => new ModuleCode(module).Value).Distinct().ToArray();
        if (selectedModules.Any(module => module == "CORE")) return BadRequest(new { code = "CORE_IS_BASELINE" });
        if (selectedModules.Any(module => modules.Modules.All(registered => registered.Code.Value != module))) return BadRequest(new { code = "MODULE_NOT_REGISTERED" });

        var plan = new Plan(code, request.Name);
        foreach (var module in selectedModules) plan.Modules.Add(new PlanModule(plan.Id, module));
        db.Plans.Add(plan);
        if (request.Entitlements is not null)
            foreach (var entitlement in request.Entitlements)
                db.PlanEntitlements.Add(new PlanEntitlement(plan.Id, entitlement.Key, entitlement.Value));
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new PlatformPlanResponse(plan.Id, plan.Code, plan.Name, selectedModules, request.Entitlements?.Select(x => new PlatformEntitlementResponse(x.Key, x.Value)).ToArray() ?? []));
    }
}

public sealed record CreatePlatformPlanRequest(string Code, string Name, IReadOnlyCollection<string> Modules, IReadOnlyDictionary<string, long>? Entitlements);
public sealed record PlatformPlanResponse(Guid Id, string Code, string Name, IReadOnlyCollection<string> Modules, IReadOnlyCollection<PlatformEntitlementResponse> Entitlements);
public sealed record PlatformEntitlementResponse(string Key, long Value);
