using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scheduling.Domain;
using Scheduling.Infrastructure;
using Scheduling.Application;
using Shine.Application;
using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/scheduling")]
public sealed class SchedulingController(SchedulingDbContext db, ICurrentTenant currentTenant, AvailabilitySlotCalculator slotCalculator, IAppointmentEventPublisher eventPublisher) : ControllerBase
{
    [HttpGet("professionals")]
    public async Task<ActionResult<PagedResponse<ProfessionalResponse>>> Professionals([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant(); var query = db.Professionals.AsNoTracking().Where(x => x.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken); var items = await query.OrderBy(x => x.Name).Skip((request.ValidatedPage - 1) * request.ValidatedPageSize).Take(request.ValidatedPageSize).Select(x => new ProfessionalResponse(x.Id, x.Name, x.UserId, x.IsActive, x.MaxConcurrentAppointments)).ToArrayAsync(cancellationToken);
        return Ok(PagedResponse<ProfessionalResponse>.Create(items, request.ValidatedPage, request.ValidatedPageSize, total));
    }

    [HttpPost("professionals")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<ProfessionalResponse>> CreateProfessional(CreateProfessionalRequest request, CancellationToken cancellationToken)
    {
        var item = new Professional(RequireTenant(), request.Name, request.UserId, request.MaxConcurrentAppointments); db.Professionals.Add(item); await db.SaveChangesAsync(cancellationToken); return Ok(new ProfessionalResponse(item.Id, item.Name, item.UserId, item.IsActive, item.MaxConcurrentAppointments));
    }

    [HttpPut("professionals/{professionalId:guid}/capacity")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<ProfessionalResponse>> UpdateProfessionalCapacity(Guid professionalId, UpdateProfessionalCapacityRequest request, CancellationToken cancellationToken)
    {
        var item = await db.Professionals.SingleOrDefaultAsync(x => x.Id == professionalId && x.TenantId == RequireTenant(), cancellationToken);
        if (item is null) return NotFound();
        item.SetMaxConcurrentAppointments(request.MaxConcurrentAppointments);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new ProfessionalResponse(item.Id, item.Name, item.UserId, item.IsActive, item.MaxConcurrentAppointments));
    }

    [HttpGet("services")]
    public async Task<ActionResult<PagedResponse<ServiceResponse>>> Services([FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant(); var query = db.Services.AsNoTracking().Where(x => x.TenantId == tenantId);
        var total = await query.CountAsync(cancellationToken); var items = await query.OrderBy(x => x.Name).Skip((request.ValidatedPage - 1) * request.ValidatedPageSize).Take(request.ValidatedPageSize).Select(x => new ServiceResponse(x.Id, x.Name, x.DurationMinutes, x.IsActive)).ToArrayAsync(cancellationToken);
        return Ok(PagedResponse<ServiceResponse>.Create(items, request.ValidatedPage, request.ValidatedPageSize, total));
    }

    [HttpPost("services")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<ServiceResponse>> CreateService(CreateServiceRequest request, CancellationToken cancellationToken)
    {
        var item = new Service(RequireTenant(), request.Name, request.DurationMinutes); db.Services.Add(item); await db.SaveChangesAsync(cancellationToken); return Ok(new ServiceResponse(item.Id, item.Name, item.DurationMinutes, item.IsActive));
    }

    [HttpGet("professionals/{professionalId:guid}/services")]
    public async Task<ActionResult<IReadOnlyCollection<ProfessionalServiceResponse>>> ProfessionalServices(Guid professionalId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var professionalExists = await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId, cancellationToken);
        if (!professionalExists) return NotFound();
        var items = await db.ProfessionalServices.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId)
            .Join(db.Services, link => link.ServiceId, service => service.Id, (link, service) => new ProfessionalServiceResponse(link.Id, service.Id, service.Name, link.DurationOverrideMinutes, link.IsActive))
            .OrderBy(x => x.Name)
            .ToArrayAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPut("professionals/{professionalId:guid}/services/{serviceId:guid}")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<ProfessionalServiceResponse>> AssociateService(Guid professionalId, Guid serviceId, AssociateServiceRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var professionalExists = await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId, cancellationToken);
        var serviceExists = await db.Services.AnyAsync(x => x.Id == serviceId && x.TenantId == tenantId, cancellationToken);
        if (!professionalExists || !serviceExists) return NotFound();
        var link = await db.ProfessionalServices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == serviceId, cancellationToken);
        if (link is null) { link = new ProfessionalService(tenantId, professionalId, serviceId, request.DurationOverrideMinutes); db.ProfessionalServices.Add(link); }
        else { link.SetDurationOverride(request.DurationOverrideMinutes); link.SetActive(true); }
        await db.SaveChangesAsync(cancellationToken);
        var service = await db.Services.AsNoTracking().SingleAsync(x => x.Id == serviceId, cancellationToken);
        return Ok(new ProfessionalServiceResponse(link.Id, service.Id, service.Name, link.DurationOverrideMinutes, link.IsActive));
    }

    [HttpDelete("professionals/{professionalId:guid}/services/{serviceId:guid}")]
    [RequiresPermission("scheduling.manage")]
    public async Task<IActionResult> DisassociateService(Guid professionalId, Guid serviceId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var link = await db.ProfessionalServices.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == serviceId, cancellationToken);
        if (link is null) return NotFound();
        link.SetActive(false); await db.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [HttpGet("professionals/{professionalId:guid}/availability")]
    public async Task<ActionResult<IReadOnlyCollection<AvailabilityRuleResponse>>> Availability(Guid professionalId, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var items = await db.AvailabilityRules.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.IsActive).OrderBy(x => x.DayOfWeek).ThenBy(x => x.StartsAt).Select(x => new AvailabilityRuleResponse(x.Id, x.DayOfWeek, x.StartsAt, x.EndsAt)).ToArrayAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost("professionals/{professionalId:guid}/availability")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<AvailabilityRuleResponse>> AddAvailability(Guid professionalId, AvailabilityRuleRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId, cancellationToken)) return NotFound();
        var overlaps = await db.AvailabilityRules.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.DayOfWeek == request.DayOfWeek && x.IsActive && request.StartsAt < x.EndsAt && request.EndsAt > x.StartsAt, cancellationToken);
        if (overlaps) return Conflict("Availability period overlaps an existing rule.");
        var item = new AvailabilityRule(tenantId, professionalId, request.DayOfWeek, request.StartsAt, request.EndsAt); db.AvailabilityRules.Add(item); await db.SaveChangesAsync(cancellationToken);
        return Ok(new AvailabilityRuleResponse(item.Id, item.DayOfWeek, item.StartsAt, item.EndsAt));
    }

    [HttpDelete("availability/{ruleId:guid}")]
    [RequiresPermission("scheduling.manage")]
    public async Task<IActionResult> RemoveAvailability(Guid ruleId, CancellationToken cancellationToken)
    {
        var item = await db.AvailabilityRules.SingleOrDefaultAsync(x => x.Id == ruleId && x.TenantId == RequireTenant(), cancellationToken);
        if (item is null) return NotFound(); item.SetActive(false); await db.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [HttpPost("professionals/{professionalId:guid}/blocks")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<ScheduleBlockResponse>> AddBlock(Guid professionalId, ScheduleBlockRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId, cancellationToken)) return NotFound();
        var overlaps = await db.ScheduleBlocks.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        if (overlaps) return Conflict("Schedule block overlaps an existing block.");
        var item = new ScheduleBlock(tenantId, professionalId, request.StartsAtUtc, request.EndsAtUtc, request.Reason); db.ScheduleBlocks.Add(item); await db.SaveChangesAsync(cancellationToken);
        return Ok(new ScheduleBlockResponse(item.Id, item.StartsAtUtc, item.EndsAtUtc, item.Reason));
    }

    [HttpGet("professionals/{professionalId:guid}/availability-exceptions")]
    public async Task<ActionResult<IReadOnlyCollection<AvailabilityExceptionResponse>>> Exceptions(Guid professionalId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var query = db.AvailabilityExceptions.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId);
        if (from is not null) query = query.Where(x => x.Date >= from.Value);
        if (to is not null) query = query.Where(x => x.Date <= to.Value);
        var items = await query.OrderBy(x => x.Date).ThenBy(x => x.StartsAt).Select(x => new AvailabilityExceptionResponse(x.Id, x.Date, x.StartsAt, x.EndsAt, x.Reason)).ToArrayAsync(cancellationToken);
        return Ok(items);
    }

    [HttpPost("professionals/{professionalId:guid}/availability-exceptions")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<AvailabilityExceptionResponse>> AddException(Guid professionalId, AvailabilityExceptionRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        if (!await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId, cancellationToken)) return NotFound();
        var overlap = await db.AvailabilityExceptions.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.Date == request.Date &&
            ((!request.StartsAt.HasValue && !x.StartsAt.HasValue) ||
             (request.StartsAt.HasValue && x.StartsAt.HasValue && request.StartsAt.Value < x.EndsAt!.Value && request.EndsAt!.Value > x.StartsAt.Value)), cancellationToken);
        if (overlap) return Conflict("Availability exception overlaps an existing exception.");
        var item = new AvailabilityException(tenantId, professionalId, request.Date, request.StartsAt, request.EndsAt, request.Reason);
        db.AvailabilityExceptions.Add(item); await db.SaveChangesAsync(cancellationToken);
        return Ok(new AvailabilityExceptionResponse(item.Id, item.Date, item.StartsAt, item.EndsAt, item.Reason));
    }

    [HttpDelete("availability-exceptions/{exceptionId:guid}")]
    [RequiresPermission("scheduling.manage")]
    public async Task<IActionResult> RemoveException(Guid exceptionId, CancellationToken cancellationToken)
    {
        var item = await db.AvailabilityExceptions.SingleOrDefaultAsync(x => x.Id == exceptionId && x.TenantId == RequireTenant(), cancellationToken);
        if (item is null) return NotFound(); db.AvailabilityExceptions.Remove(item); await db.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [HttpGet("settings")]
    public async Task<ActionResult<SchedulingSettingsResponse>> GetSettings(CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var item = await db.SchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        item ??= new SchedulingSettings(tenantId);
        return Ok(new SchedulingSettingsResponse(item.SlotIntervalMinutes, item.BufferBeforeMinutes, item.BufferAfterMinutes, item.TimeZoneId));
    }

    [HttpPut("settings")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<SchedulingSettingsResponse>> UpdateSettings(SchedulingSettingsRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var item = await db.SchedulingSettings.SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken);
        if (item is null) { item = new SchedulingSettings(tenantId); db.SchedulingSettings.Add(item); }
        item.Update(request.SlotIntervalMinutes, request.BufferBeforeMinutes, request.BufferAfterMinutes, request.TimeZoneId);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new SchedulingSettingsResponse(item.SlotIntervalMinutes, item.BufferBeforeMinutes, item.BufferAfterMinutes, item.TimeZoneId));
    }

    [HttpGet("availability/slots")]
    public async Task<ActionResult<IReadOnlyCollection<AvailabilitySlotResponse>>> Slots([FromQuery] Guid professionalId, [FromQuery] Guid serviceId, [FromQuery] DateOnly date, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var professionalExists = await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId && x.IsActive, cancellationToken);
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serviceId && x.TenantId == tenantId && x.IsActive, cancellationToken);
        if (!professionalExists || service is null) return NotFound();
        var settings = await db.SchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken) ?? new SchedulingSettings(tenantId);
        var rules = await db.AvailabilityRules.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId).ToArrayAsync(cancellationToken);
        var exceptions = await db.AvailabilityExceptions.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.Date == date).ToArrayAsync(cancellationToken);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId));
        var toUtc = fromUtc.AddDays(1);
        var blocks = await db.ScheduleBlocks.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc).ToArrayAsync(cancellationToken);
        var appointments = await db.Appointments.AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.Status != AppointmentStatus.Cancelled && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc)
            .Select(x => new { x.StartsAtUtc, x.EndsAtUtc })
            .ToArrayAsync(cancellationToken);
        var occupied = appointments.Select(x => (StartsAtUtc: x.StartsAtUtc, EndsAtUtc: x.EndsAtUtc));
        var slots = slotCalculator.Calculate(date, settings.TimeZoneId, service.DurationMinutes, settings.BufferBeforeMinutes, settings.BufferAfterMinutes, settings.SlotIntervalMinutes, rules, exceptions, blocks, occupied);
        return Ok(slots.Select(x => new AvailabilitySlotResponse(x.StartsAtUtc, x.EndsAtUtc)).ToArray());
    }

    [HttpGet("appointments")]
    public async Task<ActionResult<PagedResponse<AppointmentResponse>>> Appointments([FromQuery] DateTime fromUtc, [FromQuery] DateTime toUtc, [FromQuery] PagedRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var query = db.Appointments.AsNoTracking().Where(x => x.TenantId == tenantId && x.StartsAtUtc < toUtc && x.EndsAtUtc > fromUtc);
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(x => x.StartsAtUtc).Skip((request.ValidatedPage - 1) * request.ValidatedPageSize).Take(request.ValidatedPageSize).Select(x => new AppointmentResponse(x.Id, x.ProfessionalId, x.ServiceId, x.CustomerName, x.CustomerContact, x.StartsAtUtc, x.EndsAtUtc, x.Status, x.Version)).ToArrayAsync(cancellationToken);
        return Ok(PagedResponse<AppointmentResponse>.Create(items, request.ValidatedPage, request.ValidatedPageSize, total));
    }

    [HttpPost("appointments")]
    [RequiresPermission("scheduling.manage")]
    public async Task<ActionResult<AppointmentResponse>> CreateAppointment(CreateAppointmentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var validAssociation = await db.ProfessionalServices.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && x.ServiceId == request.ServiceId && x.IsActive, cancellationToken);
        if (!validAssociation) return BadRequest("Professional is not associated with the service.");
        var service = await db.Services.AsNoTracking().SingleAsync(x => x.Id == request.ServiceId, cancellationToken);
        var expectedEnd = request.StartsAtUtc.AddMinutes(service.DurationMinutes);
        if (expectedEnd != request.EndsAtUtc) return BadRequest("Appointment end must match the service duration.");
        var professional = await db.Professionals.SingleAsync(x => x.Id == request.ProfessionalId && x.TenantId == tenantId, cancellationToken);
        var conflictCount = await db.Appointments.CountAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && x.Status != AppointmentStatus.Cancelled && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        var blocked = await db.ScheduleBlocks.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        if (blocked) return Conflict("Appointment conflicts with a schedule block.");
        var policy = new CapacityPolicy(professional.MaxConcurrentAppointments, request.ConflictMode);
        if (policy.Blocks(conflictCount)) return Conflict(new { code = "CAPACITY_EXCEEDED", message = "Professional capacity is exceeded for this period.", current = conflictCount, capacity = policy.MaxConcurrent });
        if (policy.RequiresConfirmation(conflictCount) && !request.AllowConflict) return Conflict(new { code = "APPOINTMENT_CONFLICT", message = "Appointment overlaps an existing appointment.", current = conflictCount, capacity = policy.MaxConcurrent });
        var item = new Appointment(tenantId, request.ProfessionalId, request.ServiceId, request.CustomerName, request.CustomerContact, request.StartsAtUtc, request.EndsAtUtc);
        db.Appointments.Add(item);
        await eventPublisher.PublishAsync(new AppointmentCreatedEvent(item.Id, item.TenantId, item.ProfessionalId, item.ServiceId, item.StartsAtUtc, item.EndsAtUtc, DateTime.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new AppointmentResponse(item.Id, item.ProfessionalId, item.ServiceId, item.CustomerName, item.CustomerContact, item.StartsAtUtc, item.EndsAtUtc, item.Status, item.Version));
    }

    [HttpPut("appointments/{appointmentId:guid}/status")]
    [RequiresPermission("scheduling.manage")]
    public async Task<IActionResult> ChangeAppointmentStatus(Guid appointmentId, ChangeAppointmentStatusRequest request, CancellationToken cancellationToken)
    {
        var item = await db.Appointments.SingleOrDefaultAsync(x => x.Id == appointmentId && x.TenantId == RequireTenant(), cancellationToken);
        if (item is null) return NotFound();
        var previousStatus = item.Status; item.ChangeStatus(request.Status);
        await eventPublisher.PublishAsync(new AppointmentStatusChangedEvent(item.Id, item.TenantId, item.ProfessionalId, item.ServiceId, previousStatus.ToString(), item.Status.ToString(), DateTime.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken); return NoContent();
    }

    [HttpPut("appointments/{appointmentId:guid}/reschedule")]
    [RequiresPermission("tenant.manage")]
    public async Task<ActionResult<AppointmentResponse>> RescheduleAppointment(Guid appointmentId, RescheduleAppointmentRequest request, CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var item = await db.Appointments.SingleOrDefaultAsync(x => x.Id == appointmentId && x.TenantId == tenantId, cancellationToken);
        if (item is null) return NotFound();
        if (request.ExpectedVersion != item.Version) return Conflict(new { code = "STALE_APPOINTMENT", message = "Appointment was changed by another user.", currentVersion = item.Version });
        var professional = await db.Professionals.SingleAsync(x => x.Id == item.ProfessionalId && x.TenantId == tenantId, cancellationToken);
        var conflictCount = await db.Appointments.CountAsync(x => x.Id != appointmentId && x.TenantId == tenantId && x.ProfessionalId == item.ProfessionalId && x.Status != AppointmentStatus.Cancelled && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        var blocked = await db.ScheduleBlocks.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == item.ProfessionalId && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        if (blocked) return Conflict("Appointment conflicts with a schedule block.");
        var policy = new CapacityPolicy(professional.MaxConcurrentAppointments, request.ConflictMode);
        if (policy.Blocks(conflictCount)) return Conflict(new { code = "CAPACITY_EXCEEDED", message = "Professional capacity is exceeded for this period.", current = conflictCount, capacity = policy.MaxConcurrent });
        if (policy.RequiresConfirmation(conflictCount) && !request.AllowConflict) return Conflict(new { code = "APPOINTMENT_CONFLICT", message = "Appointment overlaps an existing appointment.", current = conflictCount, capacity = policy.MaxConcurrent });
        var previousStartsAtUtc = item.StartsAtUtc; var previousEndsAtUtc = item.EndsAtUtc;
        item.Reschedule(request.StartsAtUtc, request.EndsAtUtc);
        await eventPublisher.PublishAsync(new AppointmentRescheduledEvent(item.Id, item.TenantId, item.ProfessionalId, item.ServiceId, previousStartsAtUtc, previousEndsAtUtc, item.StartsAtUtc, item.EndsAtUtc, DateTime.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new AppointmentResponse(item.Id, item.ProfessionalId, item.ServiceId, item.CustomerName, item.CustomerContact, item.StartsAtUtc, item.EndsAtUtc, item.Status, item.Version));
    }

    private Guid RequireTenant() => currentTenant.TenantId ?? throw new TenantIsolationException("An active tenant is required.");
}

public sealed record CreateProfessionalRequest(string Name, Guid? UserId, int MaxConcurrentAppointments = 1);
public sealed record UpdateProfessionalCapacityRequest(int MaxConcurrentAppointments);
public sealed record ProfessionalResponse(Guid Id, string Name, Guid? UserId, bool IsActive, int MaxConcurrentAppointments);
public sealed record CreateServiceRequest(string Name, int DurationMinutes);
public sealed record ServiceResponse(Guid Id, string Name, int DurationMinutes, bool IsActive);
public sealed record AssociateServiceRequest(int? DurationOverrideMinutes);
public sealed record ProfessionalServiceResponse(Guid Id, Guid ServiceId, string Name, int? DurationOverrideMinutes, bool IsActive);
public sealed record AvailabilityRuleRequest(DayOfWeek DayOfWeek, TimeSpan StartsAt, TimeSpan EndsAt);
public sealed record AvailabilityRuleResponse(Guid Id, DayOfWeek DayOfWeek, TimeSpan StartsAt, TimeSpan EndsAt);
public sealed record ScheduleBlockRequest(DateTime StartsAtUtc, DateTime EndsAtUtc, string Reason);
public sealed record ScheduleBlockResponse(Guid Id, DateTime StartsAtUtc, DateTime EndsAtUtc, string Reason);
public sealed record AvailabilityExceptionRequest(DateOnly Date, TimeSpan? StartsAt, TimeSpan? EndsAt, string Reason);
public sealed record AvailabilityExceptionResponse(Guid Id, DateOnly Date, TimeSpan? StartsAt, TimeSpan? EndsAt, string Reason);
public sealed record SchedulingSettingsRequest(int SlotIntervalMinutes, int BufferBeforeMinutes, int BufferAfterMinutes, string TimeZoneId);
public sealed record SchedulingSettingsResponse(int SlotIntervalMinutes, int BufferBeforeMinutes, int BufferAfterMinutes, string TimeZoneId);
public sealed record AvailabilitySlotResponse(DateTime StartsAtUtc, DateTime EndsAtUtc);
public sealed record CreateAppointmentRequest(Guid ProfessionalId, Guid ServiceId, string CustomerName, string CustomerContact, DateTime StartsAtUtc, DateTime EndsAtUtc, bool AllowConflict = false, ConflictMode ConflictMode = ConflictMode.WarnAndConfirm);
public sealed record ChangeAppointmentStatusRequest(AppointmentStatus Status);
public sealed record AppointmentResponse(Guid Id, Guid ProfessionalId, Guid ServiceId, string CustomerName, string CustomerContact, DateTime StartsAtUtc, DateTime EndsAtUtc, AppointmentStatus Status, Guid Version);
public sealed record RescheduleAppointmentRequest(DateTime StartsAtUtc, DateTime EndsAtUtc, Guid ExpectedVersion, bool AllowConflict = false, ConflictMode ConflictMode = ConflictMode.WarnAndConfirm);
