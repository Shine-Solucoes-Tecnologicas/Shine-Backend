using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Scheduling.Application;
using Scheduling.Domain;
using Scheduling.Infrastructure;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/public/scheduling/{tenantId:guid}")]
public sealed class PublicSchedulingController(
    SchedulingDbContext db,
    AvailabilitySlotCalculator slotCalculator,
    ServiceDurationEstimator durationEstimator,
    IAppointmentEventPublisher eventPublisher,
    IModuleAccess moduleAccess) : ControllerBase
{
    [HttpGet("availability/slots")]
    public async Task<ActionResult<IReadOnlyCollection<PublicAvailabilitySlotResponse>>> Slots(
        Guid tenantId,
        [FromQuery] Guid professionalId,
        [FromQuery] Guid serviceId,
        [FromQuery] DateOnly date,
        [FromQuery] Dictionary<string, string>? attributes,
        CancellationToken cancellationToken)
    {
        if (!await moduleAccess.HasAccessAsync(tenantId, "SCHEDULING", cancellationToken)) return NotFound();
        var data = await LoadAvailabilityAsync(tenantId, professionalId, serviceId, date, attributes, cancellationToken);
        if (data is null) return NotFound();

        var slots = slotCalculator.Calculate(date, data.TimeZoneId, data.Duration.DurationMinutes,
            data.BufferBeforeMinutes, data.BufferAfterMinutes, data.SlotIntervalMinutes,
            data.Rules, data.Exceptions, data.Blocks, data.Occupied);
        return Ok(slots.Select(x => new PublicAvailabilitySlotResponse(x.StartsAtUtc, x.EndsAtUtc, data.Duration.DurationMinutes)).ToArray());
    }

    [HttpPost("appointments")]
    public async Task<ActionResult<PublicAppointmentResponse>> CreateAppointment(
        Guid tenantId,
        PublicCreateAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        if (!await moduleAccess.HasAccessAsync(tenantId, "SCHEDULING", cancellationToken)) return NotFound();
        if (request.StartsAtUtc.Kind != DateTimeKind.Utc || request.EndsAtUtc.Kind != DateTimeKind.Utc)
            return BadRequest(new { code = "UTC_REQUIRED", message = "Appointment times must be UTC." });

        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ServiceId && x.TenantId == tenantId && x.IsActive, cancellationToken);
        var professional = await db.Professionals.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ProfessionalId && x.TenantId == tenantId && x.IsActive, cancellationToken);
        var association = await db.ProfessionalServices.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && x.ServiceId == request.ServiceId && x.IsActive, cancellationToken);
        if (service is null || professional is null || association is null) return NotFound();

        var duration = durationEstimator.Estimate(new ServiceDurationContext(tenantId, request.ServiceId, request.ProfessionalId, request.Attributes ?? new Dictionary<string, string>()), association.DurationOverrideMinutes ?? service.DurationMinutes);
        if (request.EndsAtUtc != request.StartsAtUtc.AddMinutes(duration.DurationMinutes))
            return BadRequest(new { code = "INVALID_DURATION", message = "Appointment end does not match the estimated service duration.", durationMinutes = duration.DurationMinutes });

        var conflicts = await db.Appointments.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && x.Status != AppointmentStatus.Cancelled && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        var blocked = await db.ScheduleBlocks.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        if (conflicts || blocked) return Conflict(new { code = "APPOINTMENT_UNAVAILABLE", message = "The requested period is no longer available." });

        var item = new Appointment(tenantId, request.ProfessionalId, request.ServiceId, request.CustomerName, request.CustomerContact, request.StartsAtUtc, request.EndsAtUtc);
        db.Appointments.Add(item);
        await eventPublisher.PublishAsync(new AppointmentCreatedEvent(item.Id, item.TenantId, item.ProfessionalId, item.ServiceId, item.StartsAtUtc, item.EndsAtUtc, DateTime.UtcNow), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new PublicAppointmentResponse(item.Id, item.StartsAtUtc, item.EndsAtUtc, item.Status));
    }

    private async Task<AvailabilityData?> LoadAvailabilityAsync(Guid tenantId, Guid professionalId, Guid serviceId, DateOnly date, IReadOnlyDictionary<string, string>? attributes, CancellationToken cancellationToken)
    {
        var professionalExists = await db.Professionals.AnyAsync(x => x.Id == professionalId && x.TenantId == tenantId && x.IsActive, cancellationToken);
        var service = await db.Services.AsNoTracking().SingleOrDefaultAsync(x => x.Id == serviceId && x.TenantId == tenantId && x.IsActive, cancellationToken);
        var association = await db.ProfessionalServices.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == serviceId && x.IsActive, cancellationToken);
        if (!professionalExists || service is null || association is null) return null;

        var settings = await db.SchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken) ?? new SchedulingSettings(tenantId);
        var duration = durationEstimator.Estimate(new ServiceDurationContext(tenantId, serviceId, professionalId, attributes ?? new Dictionary<string, string>()), association.DurationOverrideMinutes ?? service.DurationMinutes);
        var rules = await db.AvailabilityRules.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId).ToArrayAsync(cancellationToken);
        var exceptions = await db.AvailabilityExceptions.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.Date == date).ToArrayAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        var toUtc = fromUtc.AddDays(1);
        var blocks = await db.ScheduleBlocks.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc).ToArrayAsync(cancellationToken);
        var occupied = await db.Appointments.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.Status != AppointmentStatus.Cancelled && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc).Select(x => new { x.StartsAtUtc, x.EndsAtUtc }).ToArrayAsync(cancellationToken);
        return new AvailabilityData(settings.TimeZoneId, settings.BufferBeforeMinutes, settings.BufferAfterMinutes, settings.SlotIntervalMinutes, duration, rules, exceptions, blocks, occupied.Select(x => (x.StartsAtUtc, x.EndsAtUtc)).ToArray());
    }

    private sealed record AvailabilityData(string TimeZoneId, int BufferBeforeMinutes, int BufferAfterMinutes, int SlotIntervalMinutes, ServiceDurationEstimate Duration, AvailabilityRule[] Rules, AvailabilityException[] Exceptions, ScheduleBlock[] Blocks, (DateTime StartsAtUtc, DateTime EndsAtUtc)[] Occupied);
}

public sealed record PublicCreateAppointmentRequest(Guid ProfessionalId, Guid ServiceId, string CustomerName, string CustomerContact, DateTime StartsAtUtc, DateTime EndsAtUtc, IReadOnlyDictionary<string, string>? Attributes = null);
public sealed record PublicAvailabilitySlotResponse(DateTime StartsAtUtc, DateTime EndsAtUtc, int DurationMinutes);
public sealed record PublicAppointmentResponse(Guid Id, DateTime StartsAtUtc, DateTime EndsAtUtc, AppointmentStatus Status);
