using Microsoft.EntityFrameworkCore;
using Scheduling.Application;
using Scheduling.Domain;
using Shine.Application;
using Shine.Domain;

namespace Scheduling.Infrastructure;

internal enum SchedulingWidgetKind { NextAppointments, AverageOccupancy, BusiestHours, QuietestHours }

internal sealed class SchedulingDashboardWidgetProvider(
    SchedulingDbContext db,
    IBusinessCatalogReader catalog,
    AvailabilitySlotCalculator slots,
    SchedulingWidgetKind kind) : IDashboardWidgetProvider
{
    private const int MaximumPeriodDays = 31;

    public DashboardWidgetDescriptor Descriptor { get; } = kind switch
    {
        SchedulingWidgetKind.NextAppointments => new("scheduling.next-appointments", "SCHEDULING",
            "Próximos agendamentos", "Próximos compromissos da agenda", "scheduling.read", 2, 2, 2, 1, dataSource: "scheduling.appointments"),
        SchedulingWidgetKind.AverageOccupancy => new("scheduling.average-occupancy", "SCHEDULING",
            "Ocupação média", "Percentual médio de ocupação da agenda no período", "scheduling.read", 2, 1, dataSource: "scheduling.availability"),
        SchedulingWidgetKind.BusiestHours => new("scheduling.busiest-hours", "SCHEDULING",
            "Horários de maior movimento", "Faixas horárias com mais agendamentos", "scheduling.read", 2, 2, dataSource: "scheduling.appointments"),
        SchedulingWidgetKind.QuietestHours => new("scheduling.quietest-hours", "SCHEDULING",
            "Horários de menor movimento", "Faixas horárias com menos agendamentos", "scheduling.read", 2, 2, dataSource: "scheduling.appointments"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public async Task<DashboardWidgetData> GetDataAsync(DashboardWidgetContext context, CancellationToken cancellationToken = default)
    {
        if (context.ToUtc - context.FromUtc > TimeSpan.FromDays(MaximumPeriodDays))
            throw new ArgumentException($"Scheduling widget periods cannot exceed {MaximumPeriodDays} days.");
        return kind switch
        {
            SchedulingWidgetKind.NextAppointments => await NextAppointmentsAsync(context, cancellationToken),
            SchedulingWidgetKind.AverageOccupancy => await AverageOccupancyAsync(context, cancellationToken),
            SchedulingWidgetKind.BusiestHours => await MovementHoursAsync(context, busiest: true, cancellationToken),
            SchedulingWidgetKind.QuietestHours => await MovementHoursAsync(context, busiest: false, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private async Task<DashboardWidgetData> NextAppointmentsAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
    {
        var fromUtc = context.FromUtc.UtcDateTime;
        var toUtc = context.ToUtc.UtcDateTime;
        var appointments = await db.Appointments.AsNoTracking()
            .Where(appointment => appointment.TenantId == context.TenantId && appointment.StartsAtUtc >= fromUtc && appointment.StartsAtUtc < toUtc &&
                                  (appointment.Status == AppointmentStatus.Scheduled || appointment.Status == AppointmentStatus.Confirmed))
            .OrderBy(appointment => appointment.StartsAtUtc).ThenBy(appointment => appointment.Id)
            .Take(10).ToArrayAsync(cancellationToken);
        var professionals = (await catalog.FindProfessionalsAsync(context.TenantId, appointments.Select(x => x.ProfessionalId).Distinct().ToArray(), cancellationToken))
            .ToDictionary(x => x.Id);
        var services = (await catalog.FindServicesAsync(context.TenantId, appointments.Select(x => x.ServiceId).Distinct().ToArray(), cancellationToken))
            .ToDictionary(x => x.Id);
        var items = appointments.Select(appointment => new SchedulingAppointmentWidgetItem(appointment.Id, appointment.CustomerName,
                appointment.ProfessionalId, professionals.GetValueOrDefault(appointment.ProfessionalId)?.Name ?? "Profissional indisponível",
                appointment.ServiceId, services.GetValueOrDefault(appointment.ServiceId)?.Name ?? "Serviço indisponível",
                appointment.StartsAtUtc, appointment.EndsAtUtc, appointment.Status.ToString()))
            .ToArray();
        return Data(new Dictionary<string, object?> { ["items"] = items, ["count"] = items.Length, ["limit"] = 10 });
    }

    private async Task<DashboardWidgetData> AverageOccupancyAsync(DashboardWidgetContext context, CancellationToken cancellationToken)
    {
        var fromUtc = context.FromUtc.UtcDateTime;
        var toUtc = context.ToUtc.UtcDateTime;
        var settings = await db.SchedulingSettings.AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == context.TenantId, cancellationToken);
        var timeZoneId = settings?.TimeZoneId ?? "America/Sao_Paulo";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var professionals = await catalog.ListActiveProfessionalsAsync(context.TenantId, cancellationToken);
        var professionalIds = professionals.Select(x => x.Id).ToArray();
        var professionalSettings = await db.ProfessionalSettings.AsNoTracking()
            .Where(x => x.TenantId == context.TenantId && professionalIds.Contains(x.ProfessionalId))
            .ToDictionaryAsync(x => x.ProfessionalId, x => x.MaxConcurrentAppointments, cancellationToken);
        var rules = await db.AvailabilityRules.AsNoTracking().Where(x => x.TenantId == context.TenantId && professionalIds.Contains(x.ProfessionalId)).ToArrayAsync(cancellationToken);
        var exceptions = await db.AvailabilityExceptions.AsNoTracking().Where(x => x.TenantId == context.TenantId && professionalIds.Contains(x.ProfessionalId)).ToArrayAsync(cancellationToken);
        var blocks = await db.ScheduleBlocks.AsNoTracking().Where(x => x.TenantId == context.TenantId && professionalIds.Contains(x.ProfessionalId) && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc).ToArrayAsync(cancellationToken);
        var appointments = await db.Appointments.AsNoTracking().Where(x => x.TenantId == context.TenantId && x.Status != AppointmentStatus.Cancelled && x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc).ToArrayAsync(cancellationToken);

        var localStart = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.FromUtc, zone).Date);
        var localEnd = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(context.ToUtc.AddTicks(-1), zone).Date);
        long capacityMinutes = 0;
        foreach (var professional in professionals)
        for (var date = localStart; date <= localEnd; date = date.AddDays(1))
        {
            var available = slots.Calculate(date, timeZoneId, 15, 0, 0, 15,
                rules.Where(x => x.ProfessionalId == professional.Id),
                exceptions.Where(x => x.ProfessionalId == professional.Id),
                blocks.Where(x => x.ProfessionalId == professional.Id));
            capacityMinutes += available.Where(x => x.EndsAtUtc > fromUtc && x.StartsAtUtc < toUtc)
                .Sum(x => (long)(x.EndsAtUtc - x.StartsAtUtc).TotalMinutes) *
                professionalSettings.GetValueOrDefault(professional.Id, settings?.DefaultMaxConcurrentAppointments ?? 1);
        }

        var bookedMinutes = appointments.Sum(x => Math.Max(0L,
            (long)(new[] { x.EndsAtUtc, toUtc }.Min() - new[] { x.StartsAtUtc, fromUtc }.Max()).TotalMinutes));
        var percentage = capacityMinutes == 0 ? 0m : decimal.Round(Math.Min(100m, bookedMinutes * 100m / capacityMinutes), 2);
        return Data(new Dictionary<string, object?>
        {
            ["occupancyPercentage"] = percentage,
            ["bookedMinutes"] = bookedMinutes,
            ["capacityMinutes"] = capacityMinutes,
            ["hasAvailability"] = capacityMinutes > 0
        });
    }

    private async Task<DashboardWidgetData> MovementHoursAsync(DashboardWidgetContext context, bool busiest, CancellationToken cancellationToken)
    {
        var fromUtc = context.FromUtc.UtcDateTime;
        var toUtc = context.ToUtc.UtcDateTime;
        var timeZoneId = await db.SchedulingSettings.AsNoTracking().Where(x => x.TenantId == context.TenantId)
            .Select(x => x.TimeZoneId).SingleOrDefaultAsync(cancellationToken) ?? "America/Sao_Paulo";
        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        var starts = await db.Appointments.AsNoTracking()
            .Where(x => x.TenantId == context.TenantId && x.Status != AppointmentStatus.Cancelled && x.StartsAtUtc >= fromUtc && x.StartsAtUtc < toUtc)
            .Select(x => x.StartsAtUtc).ToArrayAsync(cancellationToken);
        var counts = starts.GroupBy(x => TimeZoneInfo.ConvertTimeFromUtc(x, zone).Hour)
            .Select(x => new SchedulingMovementHourWidgetItem(x.Key, $"{x.Key:00}:00", x.Count())).ToArray();
        var selected = counts.Length == 0 ? [] : counts
            .Where(x => x.AppointmentCount == (busiest ? counts.Max(y => y.AppointmentCount) : counts.Min(y => y.AppointmentCount)))
            .OrderBy(x => x.Hour).Take(6).ToArray();
        return Data(new Dictionary<string, object?> { ["items"] = selected, ["timeZoneId"] = timeZoneId });
    }

    private DashboardWidgetData Data(IReadOnlyDictionary<string, object?> values) => new(Descriptor.WidgetKey, values);
}

public sealed record SchedulingAppointmentWidgetItem(Guid AppointmentId, string CustomerName, Guid ProfessionalId,
    string ProfessionalName, Guid ServiceId, string ServiceName, DateTime StartsAtUtc, DateTime EndsAtUtc, string Status);
public sealed record SchedulingMovementHourWidgetItem(int Hour, string Label, int AppointmentCount);
