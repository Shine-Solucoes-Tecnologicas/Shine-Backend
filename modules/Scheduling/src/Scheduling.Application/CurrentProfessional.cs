namespace Scheduling.Application;

public enum CurrentProfessionalResolutionStatus
{
    Resolved,
    MissingContext,
    NotLinked,
    Inactive
}

public sealed record CurrentProfessionalResolution(
    CurrentProfessionalResolutionStatus Status,
    Guid? ProfessionalId = null)
{
    public bool IsResolved => Status == CurrentProfessionalResolutionStatus.Resolved && ProfessionalId is not null;

    public static CurrentProfessionalResolution Resolved(Guid professionalId) =>
        professionalId == Guid.Empty
            ? throw new ArgumentException("Professional identifier is required.", nameof(professionalId))
            : new(CurrentProfessionalResolutionStatus.Resolved, professionalId);
}

public interface ICurrentProfessionalResolver
{
    Task<CurrentProfessionalResolution> ResolveAsync(CancellationToken cancellationToken = default);
}
