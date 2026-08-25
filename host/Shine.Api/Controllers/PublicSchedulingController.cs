using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using BusinessCatalog.Application;
using System.Data;
using Scheduling.Application;
using Scheduling.Domain;
using Scheduling.Infrastructure;
using Shine.Infrastructure;
using Shine.Application;

namespace Shine.Api.Controllers;

[ApiController]
[AllowAnonymous]
[EnableRateLimiting("public-scheduling")]
[Route("api/public/scheduling/{tenantId:guid}")]
public sealed class PublicSchedulingController(
    SchedulingDbContext db,
    AvailabilitySlotCalculator slotCalculator,
    ServiceDurationEstimator durationEstimator,
    IAppointmentEventPublisher eventPublisher,
    IModuleAccess moduleAccess,
    IEntitlementLimitGuard entitlementLimits,
    ITenantExecutionContext tenantExecutionContext,
    IBusinessCatalogReader? businessCatalog = null) : ControllerBase
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
        using var tenantScope = tenantExecutionContext.EnterTenant(tenantId);
        if (!await moduleAccess.HasAccessAsync(tenantId, "SCHEDULING", cancellationToken)) return NotFound();
        AvailabilityData? data;
        try { data = await LoadAvailabilityAsync(tenantId, professionalId, serviceId, date, attributes, cancellationToken); }
        catch (ArgumentException exception) { return BadRequest(new { code = "INVALID_DURATION_ATTRIBUTES", message = exception.Message }); }
        if (data is null) return NotFound();

        var slots = slotCalculator.Calculate(date, data.TimeZoneId, data.Duration.DurationMinutes,
            data.BufferBeforeMinutes, data.BufferAfterMinutes, data.SlotIntervalMinutes,
            data.Rules, data.Exceptions, data.Blocks, data.Occupied, data.MaxConcurrentAppointments, data.ConflictMode);
        return Ok(slots.Select(x => new PublicAvailabilitySlotResponse(x.StartsAtUtc, x.EndsAtUtc, data.Duration.DurationMinutes)).ToArray());
    }

    [HttpPost("appointments")]
    public async Task<ActionResult<PublicAppointmentResponse>> CreateAppointment(
        Guid tenantId,
        PublicCreateAppointmentRequest request,
        CancellationToken cancellationToken)
    {
        using var tenantScope = tenantExecutionContext.EnterTenant(tenantId);
        if (!await moduleAccess.HasAccessAsync(tenantId, "SCHEDULING", cancellationToken)) return NotFound();
        if (request.StartsAtUtc.Kind != DateTimeKind.Utc || request.EndsAtUtc.Kind != DateTimeKind.Utc)
            return BadRequest(new { code = "UTC_REQUIRED", message = "Appointment times must be UTC." });
        if (request.StartsAtUtc <= DateTime.UtcNow) return BadRequest(new { code = "PUBLIC_SLOT_EXPIRED", message = "The requested slot has expired." });
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Length > 160 || string.IsNullOrWhiteSpace(request.CustomerContact) || request.CustomerContact.Length > 200)
            return BadRequest(new { code = "INVALID_CUSTOMER_DATA", message = "Customer name or contact is invalid." });

        var service = await Catalog.FindServiceAsync(tenantId, request.ServiceId, cancellationToken);
        var professional = await Catalog.FindProfessionalAsync(tenantId, request.ProfessionalId, cancellationToken);
        var associated = await Catalog.IsActiveAssociationAsync(tenantId, request.ProfessionalId, request.ServiceId, cancellationToken);
        if (service is not { IsActive: true } || professional is not { IsActive: true } || !associated) return NotFound();

        ServiceDurationEstimate duration;
        try { duration = await EstimateDurationAsync(tenantId, request.ProfessionalId, service, request.Attributes, cancellationToken); }
        catch (ArgumentException exception) { return BadRequest(new { code = "INVALID_DURATION_ATTRIBUTES", message = exception.Message }); }
        if (request.EndsAtUtc != request.StartsAtUtc.AddMinutes(duration.DurationMinutes))
            return BadRequest(new { code = "INVALID_DURATION", message = "Appointment end does not match the estimated service duration.", durationMinutes = duration.DurationMinutes });

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var lockKey = $"scheduling:{tenantId:N}:{request.ProfessionalId:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({lockKey}, 0))", cancellationToken);

        var settings = await db.SchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken) ?? new SchedulingSettings(tenantId);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(request.StartsAtUtc, zone));
        var availability = await LoadAvailabilityAsync(tenantId, request.ProfessionalId, request.ServiceId, localDate, request.Attributes, cancellationToken);
        if (availability is null) return NotFound();
        var validSlot = slotCalculator.Calculate(localDate, availability.TimeZoneId, availability.Duration.DurationMinutes,
            availability.BufferBeforeMinutes, availability.BufferAfterMinutes, availability.SlotIntervalMinutes,
            availability.Rules, availability.Exceptions, availability.Blocks, availability.Occupied,
            availability.MaxConcurrentAppointments, availability.ConflictMode)
            .Any(slot => slot.StartsAtUtc == request.StartsAtUtc && slot.EndsAtUtc == request.EndsAtUtc);
        if (!validSlot) return Conflict(new { code = "APPOINTMENT_UNAVAILABLE", message = "The requested period is outside current availability." });

        var conflictCount = await db.Appointments.CountAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && x.Status != AppointmentStatus.Cancelled && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        var blocked = await db.ScheduleBlocks.AnyAsync(x => x.TenantId == tenantId && x.ProfessionalId == request.ProfessionalId && request.StartsAtUtc < x.EndsAtUtc && request.EndsAtUtc > x.StartsAtUtc, cancellationToken);
        var capacity = await GetProfessionalCapacityAsync(tenantId, request.ProfessionalId, settings.DefaultMaxConcurrentAppointments, cancellationToken);
        var policy = new Shine.Domain.CapacityPolicy(capacity, settings.ConflictMode);
        if (blocked) return Conflict(new { code = "APPOINTMENT_UNAVAILABLE", message = "The requested period is no longer available." });
        if (policy.IsCapacityExceeded(conflictCount)) return Conflict(new { code = "CAPACITY_EXCEEDED", message = "Professional capacity is exceeded for this period.", current = conflictCount, capacity = policy.MaxConcurrent });
        if (policy.ConflictMode == Shine.Domain.ConflictMode.Block && conflictCount > 0) return Conflict(new { code = "APPOINTMENT_CONFLICT", message = "The scheduling policy blocks overlapping appointments." });
        if (policy.RequiresConfirmation(conflictCount) && !request.AllowConflict) return Conflict(new { code = "APPOINTMENT_CONFLICT", message = "Confirmation is required for an overlapping appointment." });

        var item = new Appointment(tenantId, request.ProfessionalId, request.ServiceId, request.CustomerName, request.CustomerContact, request.StartsAtUtc, request.EndsAtUtc);
        var reservation = await entitlementLimits.TryReserveAsync(tenantId, Shine.Domain.EntitlementKeys.SchedulingActiveAppointments, item.Id, cancellationToken: cancellationToken);
        if (!reservation.Allowed)
            return Conflict(new
            {
                code = reservation.Status switch
                {
                    Shine.Domain.EntitlementLimitStatus.NotConfigured => "ENTITLEMENT_LIMIT_NOT_CONFIGURED",
                    Shine.Domain.EntitlementLimitStatus.Unavailable => "ENTITLEMENT_LIMIT_UNAVAILABLE",
                    _ => "ENTITLEMENT_LIMIT_EXHAUSTED"
                },
                key = reservation.Key,
                status = reservation.Status.ToString(),
                limit = reservation.Limit,
                current = reservation.CurrentUsage
            });

        try
        {
            db.Appointments.Add(item);
            await eventPublisher.PublishAsync(new AppointmentCreatedEvent(item.Id, item.TenantId, item.ProfessionalId, item.ServiceId, item.StartsAtUtc, item.EndsAtUtc, DateTime.UtcNow, item.CustomerId), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            if (reservation.Allowed)
                await entitlementLimits.ReleaseAsync(tenantId, Shine.Domain.EntitlementKeys.SchedulingActiveAppointments, item.Id, CancellationToken.None);
            throw;
        }
        return Ok(new PublicAppointmentResponse(item.Id, item.StartsAtUtc, item.EndsAtUtc, item.Status));
    }

    private async Task<AvailabilityData?> LoadAvailabilityAsync(Guid tenantId, Guid professionalId, Guid serviceId, DateOnly date, IReadOnlyDictionary<string, string>? attributes, CancellationToken cancellationToken)
    {
        var professional = await Catalog.FindProfessionalAsync(tenantId, professionalId, cancellationToken);
        var service = await Catalog.FindServiceAsync(tenantId, serviceId, cancellationToken);
        var associated = await Catalog.IsActiveAssociationAsync(tenantId, professionalId, serviceId, cancellationToken);
        if (professional is not { IsActive: true } || service is not { IsActive: true } || !associated) return null;

        var settings = await db.SchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId, cancellationToken) ?? new SchedulingSettings(tenantId);
        var duration = await EstimateDurationAsync(tenantId, professionalId, service, attributes, cancellationToken);
        var rules = await db.AvailabilityRules.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.IsActive).ToArrayAsync(cancellationToken);
        var exceptions = await db.AvailabilityExceptions.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.Date == date).ToArrayAsync(cancellationToken);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var fromUtc = TimeZoneInfo.ConvertTimeToUtc(date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone);
        var toUtc = fromUtc.AddDays(1);
        var blocks = await db.ScheduleBlocks.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc).ToArrayAsync(cancellationToken);
        var occupied = await db.Appointments.AsNoTracking().Where(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.Status != AppointmentStatus.Cancelled && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc).Select(x => new { x.StartsAtUtc, x.EndsAtUtc }).ToArrayAsync(cancellationToken);
        var capacity = await GetProfessionalCapacityAsync(tenantId, professionalId, settings.DefaultMaxConcurrentAppointments, cancellationToken);
        return new AvailabilityData(settings.TimeZoneId, settings.BufferBeforeMinutes, settings.BufferAfterMinutes, settings.SlotIntervalMinutes,
            capacity, settings.ConflictMode, duration, rules, exceptions, blocks, occupied.Select(x => (x.StartsAtUtc, x.EndsAtUtc)).ToArray());
    }

    private sealed record AvailabilityData(string TimeZoneId, int BufferBeforeMinutes, int BufferAfterMinutes, int SlotIntervalMinutes,
        int MaxConcurrentAppointments, Shine.Domain.ConflictMode ConflictMode, ServiceDurationEstimate Duration,
        AvailabilityRule[] Rules, AvailabilityException[] Exceptions, ScheduleBlock[] Blocks, (DateTime StartsAtUtc, DateTime EndsAtUtc)[] Occupied);

    private IBusinessCatalogReader Catalog => businessCatalog ?? throw new InvalidOperationException("Business catalog reader is required.");

    private async Task<int> GetProfessionalCapacityAsync(Guid tenantId, Guid professionalId, int fallback, CancellationToken cancellationToken) =>
        (await db.ProfessionalSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId, cancellationToken))?.MaxConcurrentAppointments ?? fallback;

    private async Task<ServiceDurationEstimate> EstimateDurationAsync(Guid tenantId, Guid professionalId, ServiceCatalogEntry service, IReadOnlyDictionary<string, string>? attributes, CancellationToken cancellationToken)
    {
        var policySettings = await db.ServiceSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ServiceId == service.Id, cancellationToken);
        var associationSettings = await db.ProfessionalServiceSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.ProfessionalId == professionalId && x.ServiceId == service.Id, cancellationToken);
        var policy = policySettings?.DurationAttributeKey is null ? null : new ServiceDurationPolicy(
            policySettings.DurationAttributeKey, policySettings.MinutesPerAttributeUnit!.Value,
            policySettings.MinimumDurationMinutes!.Value, policySettings.MaximumDurationMinutes!.Value,
            policySettings.DurationRuleVersion!).Validate();
        return durationEstimator.Estimate(new ServiceDurationContext(tenantId, service.Id, professionalId,
            attributes ?? new Dictionary<string, string>(), policy), associationSettings?.DurationOverrideMinutes ?? service.DurationMinutes);
    }
}

public sealed record PublicCreateAppointmentRequest(Guid ProfessionalId, Guid ServiceId, string CustomerName, string CustomerContact,
    DateTime StartsAtUtc, DateTime EndsAtUtc, IReadOnlyDictionary<string, string>? Attributes = null, bool AllowConflict = false);
public sealed record PublicAvailabilitySlotResponse(DateTime StartsAtUtc, DateTime EndsAtUtc, int DurationMinutes);
public sealed record PublicAppointmentResponse(Guid Id, DateTime StartsAtUtc, DateTime EndsAtUtc, AppointmentStatus Status);
