using Scheduling.Application;
using Shine.Domain.Authorization;
using Shine.Infrastructure;

namespace Shine.Api;

[AttributeUsage(AttributeTargets.Method)]
public sealed class SchedulingAccessAttribute(string requirement) : Attribute
{
    public string Requirement { get; } = requirement;
}

public enum SchedulingScopeDecisionStatus
{
    Allowed,
    Forbidden,
    Hidden,
    ProfessionalContextRequired
}

public sealed record SchedulingScopeDecision(
    SchedulingScopeDecisionStatus Status,
    PermissionScope? Scope = null,
    Guid? ProfessionalId = null)
{
    public bool Allowed => Status == SchedulingScopeDecisionStatus.Allowed;
}

public interface ISchedulingScopeAuthorization
{
    Task<SchedulingScopeDecision> AuthorizeAsync(
        string permissionCode,
        Guid? requestedProfessionalId = null,
        CancellationToken cancellationToken = default);
    Task<SchedulingScopeDecision> AuthorizeAllAsync(
        string permissionCode,
        CancellationToken cancellationToken = default);
}

public sealed class SchedulingScopeAuthorization(
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IPermissionAuthorization permissions,
    ICurrentProfessionalResolver currentProfessional) : ISchedulingScopeAuthorization
{
    public async Task<SchedulingScopeDecision> AuthorizeAsync(
        string permissionCode,
        Guid? requestedProfessionalId = null,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId || currentTenant.TenantId is not Guid tenantId ||
            !currentTenant.HasCompleteContext)
            return new(SchedulingScopeDecisionStatus.Forbidden);

        var scope = await permissions.GetPermissionScopeAsync(userId, tenantId, permissionCode, cancellationToken);
        if (scope is null) return new(SchedulingScopeDecisionStatus.Forbidden);
        if (scope == PermissionScope.All)
            return new(SchedulingScopeDecisionStatus.Allowed, scope, requestedProfessionalId);

        var resolution = await currentProfessional.ResolveAsync(cancellationToken);
        if (!resolution.IsResolved)
            return new(SchedulingScopeDecisionStatus.ProfessionalContextRequired, scope);
        if (requestedProfessionalId is Guid requested && requested != resolution.ProfessionalId)
            return new(SchedulingScopeDecisionStatus.Hidden, scope);

        return new(SchedulingScopeDecisionStatus.Allowed, scope, resolution.ProfessionalId);
    }

    public async Task<SchedulingScopeDecision> AuthorizeAllAsync(
        string permissionCode,
        CancellationToken cancellationToken = default)
    {
        if (currentUser.UserId is not Guid userId || currentTenant.TenantId is not Guid tenantId ||
            !currentTenant.HasCompleteContext)
            return new(SchedulingScopeDecisionStatus.Forbidden);
        var scope = await permissions.GetPermissionScopeAsync(userId, tenantId, permissionCode, cancellationToken);
        return scope == PermissionScope.All
            ? new(SchedulingScopeDecisionStatus.Allowed, scope)
            : new(SchedulingScopeDecisionStatus.Forbidden, scope);
    }
}
