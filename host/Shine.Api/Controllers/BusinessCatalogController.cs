using BusinessCatalog.Domain;
using BusinessCatalog.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shine.Api;
using Shine.Application;
using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/business-catalog")]
[Route("api/business-catalog")]
public sealed class BusinessCatalogController(
    BusinessCatalogDbContext db,
    ICurrentTenant currentTenant,
    IUserUnitReferenceValidator userReferences) : ControllerBase, IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        if (Request.Path.StartsWithSegments("/api/business-catalog"))
        {
            Response.Headers["Deprecation"] = "true";
            Response.Headers.Link = "</api/v1/business-catalog>; rel=\"successor-version\"";
        }
    }
    public void OnActionExecuted(ActionExecutedContext context) { }

    [HttpGet("professionals")]
    [RequiresPermission("business-catalog.read")]
    public async Task<ActionResult<PagedResponse<ProfessionalResponse>>> Professionals([FromQuery] PagedRequest request, [FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var query = db.Professionals.AsNoTracking().Where(x => x.TenantId == RequireTenant());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 160) return BadRequest(Error("INVALID_FILTER", "Search cannot exceed 160 characters."));
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%"));
        }
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).Skip((request.ValidatedPage - 1) * request.ValidatedPageSize)
            .Take(request.ValidatedPageSize).Select(x => new ProfessionalResponse(x.Id, x.Name, x.UserId, x.IsActive)).ToArrayAsync(cancellationToken);
        return Ok(PagedResponse<ProfessionalResponse>.Create(items, request.ValidatedPage, request.ValidatedPageSize, total));
    }

    [HttpGet("professionals/{professionalId:guid}")]
    [RequiresPermission("business-catalog.read")]
    public async Task<ActionResult<ProfessionalResponse>> Professional(Guid professionalId, CancellationToken cancellationToken)
    {
        var item = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == RequireTenant() && x.Id == professionalId, cancellationToken);
        return item is null ? NotFound(Error("PROFESSIONAL_NOT_FOUND", "Professional was not found.")) : Ok(ToResponse(item));
    }

    [HttpPost("professionals")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<ActionResult<ProfessionalResponse>> CreateProfessional(CreateProfessionalRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await IsAvailableUserAsync(tenantId, request.UserId, cancellationToken))
            return BadRequest(Error("USER_NOT_AVAILABLE_FOR_UNIT", "The selected user is not active in this unit."));

        try
        {
            var item = new Professional(tenantId, request.Name, request.UserId);
            db.Professionals.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            return Created($"/api/v1/business-catalog/professionals/{item.Id}", ToResponse(item));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { return Conflict(Error("PROFESSIONAL_ALREADY_EXISTS", "A professional with this name already exists.")); }
        catch (ArgumentException exception) { return BadRequest(new { code = "INVALID_PROFESSIONAL", message = exception.Message }); }
    }

    [HttpPut("professionals/{professionalId:guid}")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<ActionResult<ProfessionalResponse>> UpdateProfessional(Guid professionalId, UpdateProfessionalRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var item = await db.Professionals.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == professionalId, cancellationToken);
        if (item is null) return NotFound(Error("PROFESSIONAL_NOT_FOUND", "Professional was not found."));
        if (!await IsAvailableUserAsync(tenantId, request.UserId, cancellationToken))
            return BadRequest(Error("USER_NOT_AVAILABLE_FOR_UNIT", "The selected user is not active in this unit."));
        try { item.Update(request.Name, request.UserId); await db.SaveChangesAsync(cancellationToken); return Ok(ToResponse(item)); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { return Conflict(Error("PROFESSIONAL_ALREADY_EXISTS", "A professional with this name already exists.")); }
        catch (ArgumentException exception) { return BadRequest(Error("INVALID_PROFESSIONAL", exception.Message)); }
    }

    [HttpPut("professionals/{professionalId:guid}/activation")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<ActionResult<ProfessionalResponse>> SetProfessionalActivation(Guid professionalId, ActivationRequest request, CancellationToken cancellationToken)
    {
        var item = await db.Professionals.SingleOrDefaultAsync(x => x.TenantId == RequireTenant() && x.Id == professionalId, cancellationToken);
        if (item is null) return NotFound(Error("PROFESSIONAL_NOT_FOUND", "Professional was not found."));
        item.SetActive(request.IsActive); await db.SaveChangesAsync(cancellationToken); return Ok(ToResponse(item));
    }

    [HttpDelete("professionals/{professionalId:guid}")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<IActionResult> DeleteProfessional(Guid professionalId, CancellationToken cancellationToken)
    {
        var item = await db.Professionals.SingleOrDefaultAsync(x => x.TenantId == RequireTenant() && x.Id == professionalId, cancellationToken);
        if (item is not null && item.IsActive) { item.SetActive(false); await db.SaveChangesAsync(cancellationToken); }
        return NoContent();
    }

    [HttpGet("services")]
    [RequiresPermission("business-catalog.read")]
    public async Task<ActionResult<PagedResponse<ServiceResponse>>> Services([FromQuery] PagedRequest request, [FromQuery] string? search, [FromQuery] bool? isActive, CancellationToken cancellationToken)
    {
        var query = db.Services.AsNoTracking().Where(x => x.TenantId == RequireTenant());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            if (term.Length > 160) return BadRequest(Error("INVALID_FILTER", "Search cannot exceed 160 characters."));
            query = query.Where(x => EF.Functions.ILike(x.Name, $"%{term}%"));
        }
        if (isActive.HasValue) query = query.Where(x => x.IsActive == isActive.Value);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.Name).Skip((request.ValidatedPage - 1) * request.ValidatedPageSize)
            .Take(request.ValidatedPageSize).Select(x => new ServiceResponse(x.Id, x.Name, x.DurationMinutes, x.IsActive)).ToArrayAsync(cancellationToken);
        return Ok(PagedResponse<ServiceResponse>.Create(items, request.ValidatedPage, request.ValidatedPageSize, total));
    }

    [HttpGet("services/{serviceId:guid}")]
    [RequiresPermission("business-catalog.read")]
    public async Task<ActionResult<ServiceResponse>> Service(Guid serviceId, CancellationToken cancellationToken)
    {
        var item = await db.Services.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == RequireTenant() && x.Id == serviceId, cancellationToken);
        return item is null ? NotFound(Error("SERVICE_NOT_FOUND", "Service was not found.")) : Ok(ToResponse(item));
    }

    [HttpPost("services")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<ActionResult<ServiceResponse>> CreateService(CreateServiceRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var item = new Service(RequireTenant(), request.Name, request.DurationMinutes);
            db.Services.Add(item);
            await db.SaveChangesAsync(cancellationToken);
            return Created($"/api/v1/business-catalog/services/{item.Id}", ToResponse(item));
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { return Conflict(Error("SERVICE_ALREADY_EXISTS", "A service with this name already exists.")); }
        catch (ArgumentException exception) { return BadRequest(new { code = "INVALID_SERVICE", message = exception.Message }); }
    }

    [HttpPut("services/{serviceId:guid}")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<ActionResult<ServiceResponse>> UpdateService(Guid serviceId, UpdateServiceRequest request, CancellationToken cancellationToken)
    {
        var item = await db.Services.SingleOrDefaultAsync(x => x.TenantId == RequireTenant() && x.Id == serviceId, cancellationToken);
        if (item is null) return NotFound(Error("SERVICE_NOT_FOUND", "Service was not found."));
        try { item.Rename(request.Name); item.SetDuration(request.DurationMinutes); await db.SaveChangesAsync(cancellationToken); return Ok(ToResponse(item)); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception)) { return Conflict(Error("SERVICE_ALREADY_EXISTS", "A service with this name already exists.")); }
        catch (ArgumentException exception) { return BadRequest(Error("INVALID_SERVICE", exception.Message)); }
    }

    [HttpPut("services/{serviceId:guid}/activation")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<ActionResult<ServiceResponse>> SetServiceActivation(Guid serviceId, ActivationRequest request, CancellationToken cancellationToken)
    {
        var item = await db.Services.SingleOrDefaultAsync(x => x.TenantId == RequireTenant() && x.Id == serviceId, cancellationToken);
        if (item is null) return NotFound(Error("SERVICE_NOT_FOUND", "Service was not found."));
        item.SetActive(request.IsActive); await db.SaveChangesAsync(cancellationToken); return Ok(ToResponse(item));
    }

    [HttpDelete("services/{serviceId:guid}")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<IActionResult> DeleteService(Guid serviceId, CancellationToken cancellationToken)
    {
        var item = await db.Services.SingleOrDefaultAsync(x => x.TenantId == RequireTenant() && x.Id == serviceId, cancellationToken);
        if (item is not null && item.IsActive) { item.SetActive(false); await db.SaveChangesAsync(cancellationToken); }
        return NoContent();
    }

    [HttpGet("professionals/{professionalId:guid}/services")]
    [RequiresPermission("business-catalog.read")]
    public async Task<ActionResult<IReadOnlyCollection<ProfessionalServiceResponse>>> ProfessionalServices(Guid professionalId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId, cancellationToken)) return NotFound(Error("PROFESSIONAL_NOT_FOUND", "Professional was not found."));
        var items = await db.ProfessionalServices.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId)
            .Join(db.Services, link => link.ServiceId, service => service.Id,
                (link, service) => new ProfessionalServiceResponse(link.Id, service.Id, service.Name, link.IsActive))
            .OrderBy(x => x.Name).ToArrayAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPut("professionals/{professionalId:guid}/services/{serviceId:guid}")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<ActionResult<ProfessionalServiceResponse>> AssociateService(Guid professionalId, Guid serviceId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var professionalExists = await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId, cancellationToken);
        var service = await db.Services.SingleOrDefaultAsync(x => x.Id == serviceId && x.TenantId == tenantId, cancellationToken);
        if (!professionalExists) return NotFound(Error("PROFESSIONAL_NOT_FOUND", "Professional was not found."));
        if (service is null) return NotFound(Error("SERVICE_NOT_FOUND", "Service was not found."));
        var link = await db.ProfessionalServices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == serviceId, cancellationToken);
        if (link is null) { link = new ProfessionalService(tenantId, professionalId, serviceId); db.ProfessionalServices.Add(link); }
        else link.SetActive(true);
        try { await db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            db.ChangeTracker.Clear();
            link = await db.ProfessionalServices.SingleAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == serviceId, cancellationToken);
            if (!link.IsActive) { link.SetActive(true); await db.SaveChangesAsync(cancellationToken); }
        }
        return Ok(new ProfessionalServiceResponse(link.Id, service.Id, service.Name, link.IsActive));
    }

    [HttpDelete("professionals/{professionalId:guid}/services/{serviceId:guid}")]
    [RequiresPermission("business-catalog.manage")]
    public async Task<IActionResult> DisassociateService(Guid professionalId, Guid serviceId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var link = await db.ProfessionalServices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == serviceId, cancellationToken);
        if (link is not null && link.IsActive) { link.SetActive(false); await db.SaveChangesAsync(cancellationToken); }
        return NoContent();
    }

    private static ProfessionalResponse ToResponse(Professional item) => new(item.Id, item.Name, item.UserId, item.IsActive);
    private static ServiceResponse ToResponse(Service item) => new(item.Id, item.Name, item.DurationMinutes, item.IsActive);
    private static object Error(string code, string message) => new { code, message };
    private async Task<bool> IsAvailableUserAsync(Guid unitId, Guid? userId, CancellationToken cancellationToken) =>
        userId is not Guid selectedUserId ||
        await userReferences.IsActiveInUnitAsync(unitId, selectedUserId, cancellationToken);
    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    private Guid RequireTenant() => currentTenant.TenantId ?? throw new TenantIsolationException("An active tenant is required.");
}

public sealed record CreateProfessionalRequest(string Name, Guid? UserId);
public sealed record UpdateProfessionalRequest(string Name, Guid? UserId);
public sealed record ProfessionalResponse(Guid Id, string Name, Guid? UserId, bool IsActive);
public sealed record CreateServiceRequest(string Name, int DurationMinutes);
public sealed record UpdateServiceRequest(string Name, int DurationMinutes);
public sealed record ServiceResponse(Guid Id, string Name, int DurationMinutes, bool IsActive);
public sealed record ProfessionalServiceResponse(Guid Id, Guid ServiceId, string Name, bool IsActive);
public sealed record ActivationRequest(bool IsActive);
