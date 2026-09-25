using BusinessCatalog.Application;
using Scheduling.Application;
using Shine.Infrastructure;

namespace Scheduling.Infrastructure;

public sealed class CurrentProfessionalResolver(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IBusinessCatalogReader catalog) : ICurrentProfessionalResolver
{
    public async Task<CurrentProfessionalResolution> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not Guid userId ||
            !currentTenant.HasCompleteContext || currentTenant.TenantId is not Guid tenantId)
            return new(CurrentProfessionalResolutionStatus.MissingContext);

        var professional = await catalog.FindProfessionalByUserAsync(tenantId, userId, cancellationToken);
        if (professional is null) return new(CurrentProfessionalResolutionStatus.NotLinked);
        if (!professional.IsActive) return new(CurrentProfessionalResolutionStatus.Inactive);
        return CurrentProfessionalResolution.Resolved(professional.Id);
    }
}
