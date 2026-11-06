using Microsoft.AspNetCore.Mvc;
using Scheduling.Application;
using Scheduling.Domain;
using System.Text.Json.Serialization;

namespace Shine.Api.Controllers;

public static class SchedulingProblemCodes
{
    public const string AppointmentConflict = "APPOINTMENT_CONFLICT";
    public const string CapacityExceeded = "CAPACITY_EXCEEDED";
    public const string ScheduleBlocked = "SCHEDULE_BLOCKED";
    public const string InvalidAppointmentTransition = "INVALID_APPOINTMENT_TRANSITION";
    public const string SlotUnavailable = "SLOT_UNAVAILABLE";
    public const string StaleAppointment = "STALE_APPOINTMENT";
}

public sealed record SchedulingProblemDetails(
    string Type,
    string Title,
    int Status,
    string Code,
    string Detail,
    object Details);

// Used by OpenAPI to publish one discriminated schema per stable Scheduling code.
public sealed record SchedulingProblemContract<TDetails>(
    string Type,
    string Title,
    int Status,
    string Code,
    string Detail,
    TDetails Details);

public sealed record SchedulingInterval(DateTime StartUtc, DateTime EndUtc);
public sealed record RequestedInterval(DateTime StartUtc, DateTime EndUtc);
public sealed record AppointmentConflictDetails(
    RequestedInterval RequestedInterval,
    IReadOnlyCollection<SchedulingInterval> ConflictingIntervals,
    bool ConfirmationRequired,
    bool HasMoreConflicts);
public sealed record CapacityExceededDetails(
    int EffectiveCapacity,
    int ResultingPeakOccupancy,
    IReadOnlyCollection<SchedulingInterval> SaturatedBuckets);
public sealed record ScheduleBlockedDetails(
    RequestedInterval RequestedInterval,
    IReadOnlyCollection<SchedulingInterval> BlockedIntervals);
public sealed record StaleAppointmentDetails(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? CurrentVersion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] AppointmentStatus? CurrentStatus = null);
public sealed record InvalidAppointmentTransitionDetails(
    AppointmentStatus CurrentStatus,
    AppointmentStatus RequestedStatus,
    IReadOnlyCollection<AppointmentStatus> AllowedTransitions);
public sealed record SlotUnavailableDetails(RequestedInterval RequestedInterval, string Reason);

internal static class SchedulingProblems
{
    private const int MaximumIntervals = 20;

    public static ObjectResult AppointmentConflict(
        DateTime startsAtUtc,
        DateTime endsAtUtc,
        IReadOnlyCollection<SchedulingInterval> conflicts,
        bool confirmationRequired)
    {
        var ordered = conflicts.OrderBy(x => x.StartUtc).ThenBy(x => x.EndUtc).ToArray();
        return Create(StatusCodes.Status409Conflict, SchedulingProblemCodes.AppointmentConflict,
            "Appointment conflict",
            "The requested period overlaps another appointment under the current conflict policy.",
            new AppointmentConflictDetails(new(startsAtUtc, endsAtUtc), ordered.Take(MaximumIntervals).ToArray(),
                confirmationRequired, ordered.Length > MaximumIntervals));
    }

    public static ObjectResult CapacityExceeded(int capacity, int peak, IReadOnlyCollection<TimeBucket> buckets) =>
        Create(StatusCodes.Status409Conflict, SchedulingProblemCodes.CapacityExceeded,
            "Professional capacity exceeded",
            "The requested period would exceed the professional's effective capacity.",
            new CapacityExceededDetails(capacity, peak, buckets.OrderBy(x => x.StartUtc)
                .Select(x => new SchedulingInterval(x.StartUtc, x.EndUtc)).ToArray()));

    public static ObjectResult ScheduleBlocked(DateTime startsAtUtc, DateTime endsAtUtc, IReadOnlyCollection<SchedulingInterval> blocks) =>
        Create(StatusCodes.Status409Conflict, SchedulingProblemCodes.ScheduleBlocked,
            "Schedule blocked",
            "The requested period intersects a schedule block.",
            new ScheduleBlockedDetails(new(startsAtUtc, endsAtUtc), blocks.OrderBy(x => x.StartUtc).ToArray()));

    public static ObjectResult StaleAppointment(Guid? currentVersion, AppointmentStatus? currentStatus = null) =>
        Create(StatusCodes.Status412PreconditionFailed, SchedulingProblemCodes.StaleAppointment,
            "Appointment version is stale",
            "The appointment changed after the version supplied by the client.",
            new StaleAppointmentDetails(currentVersion, currentStatus));

    public static ObjectResult InvalidTransition(AppointmentStatus current, AppointmentStatus requested) =>
        Create(StatusCodes.Status409Conflict, SchedulingProblemCodes.InvalidAppointmentTransition,
            "Invalid appointment transition",
            "The requested status transition is not allowed from the current status.",
            new InvalidAppointmentTransitionDetails(current, requested, AllowedTransitions(current)));

    public static bool CanTransition(AppointmentStatus current, AppointmentStatus requested) =>
        AllowedTransitions(current).Contains(requested);

    public static ObjectResult SlotUnavailable(DateTime startsAtUtc, DateTime endsAtUtc, string reason) =>
        Create(StatusCodes.Status409Conflict, SchedulingProblemCodes.SlotUnavailable,
            "Slot unavailable",
            "The requested period is not available for booking.",
            new SlotUnavailableDetails(new(startsAtUtc, endsAtUtc), reason));

    public static ObjectResult Validation(string code, string detail) =>
        Create(StatusCodes.Status400BadRequest, code, "Scheduling validation failed", detail,
            new { });

    private static ObjectResult Create(int status, string code, string title, string detail, object details)
    {
        var result = new ObjectResult(new SchedulingProblemDetails(
            $"urn:shine:problem:{code.ToLowerInvariant().Replace('_', '-')}", title, status, code, detail, details))
        {
            StatusCode = status
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private static AppointmentStatus[] AllowedTransitions(AppointmentStatus status) => status switch
    {
        AppointmentStatus.Scheduled => [AppointmentStatus.Confirmed, AppointmentStatus.Cancelled, AppointmentStatus.NoShow],
        AppointmentStatus.Confirmed => [AppointmentStatus.Completed, AppointmentStatus.Cancelled, AppointmentStatus.NoShow],
        AppointmentStatus.Cancelled => [AppointmentStatus.Scheduled],
        _ => []
    };
}
