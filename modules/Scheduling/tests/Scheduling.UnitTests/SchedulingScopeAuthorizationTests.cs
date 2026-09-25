using Scheduling.Application;
using Shine.Api;
using Shine.Domain.Authorization;
using Shine.Infrastructure;

namespace Scheduling.UnitTests;

public sealed class SchedulingScopeAuthorizationTests
{
    private readonly Guid userId = Guid.NewGuid();
    private readonly Guid tenantId = Guid.NewGuid();
    private readonly Guid professionalId = Guid.NewGuid();

    [Fact]
    public async Task All_scope_accepts_any_professional_without_resolving_a_personal_link()
    {
        var requested = Guid.NewGuid();
        var service = Create(PermissionScope.All, new(CurrentProfessionalResolutionStatus.NotLinked));

        var decision = await service.AuthorizeAsync("scheduling.manage", requested);

        Assert.True(decision.Allowed);
        Assert.Equal(PermissionScope.All, decision.Scope);
        Assert.Equal(requested, decision.ProfessionalId);
    }

    [Fact]
    public async Task Own_scope_forces_the_professional_resolved_by_the_server()
    {
        var service = Create(PermissionScope.Own, CurrentProfessionalResolution.Resolved(professionalId));

        var decision = await service.AuthorizeAsync("scheduling.read");

        Assert.True(decision.Allowed);
        Assert.Equal(PermissionScope.Own, decision.Scope);
        Assert.Equal(professionalId, decision.ProfessionalId);
    }

    [Fact]
    public async Task Own_scope_hides_a_different_professional_identifier()
    {
        var service = Create(PermissionScope.Own, CurrentProfessionalResolution.Resolved(professionalId));

        var decision = await service.AuthorizeAsync("scheduling.manage", Guid.NewGuid());

        Assert.Equal(SchedulingScopeDecisionStatus.Hidden, decision.Status);
    }

    [Theory]
    [InlineData(CurrentProfessionalResolutionStatus.NotLinked)]
    [InlineData(CurrentProfessionalResolutionStatus.Inactive)]
    [InlineData(CurrentProfessionalResolutionStatus.MissingContext)]
    public async Task Own_scope_requires_an_active_professional_context(CurrentProfessionalResolutionStatus status)
    {
        var service = Create(PermissionScope.Own, new(status));

        var decision = await service.AuthorizeAsync("scheduling.read");

        Assert.Equal(SchedulingScopeDecisionStatus.ProfessionalContextRequired, decision.Status);
    }

    [Fact]
    public async Task Missing_permission_is_forbidden_before_professional_resolution()
    {
        var resolver = new StubProfessionalResolver(CurrentProfessionalResolution.Resolved(professionalId));
        var service = new SchedulingScopeAuthorization(new StubUser(userId), new StubTenant(tenantId),
            new StubPermissions(null), resolver);

        var decision = await service.AuthorizeAsync("scheduling.configure", professionalId);

        Assert.Equal(SchedulingScopeDecisionStatus.Forbidden, decision.Status);
        Assert.Equal(0, resolver.Calls);
    }

    [Fact]
    public async Task Administrative_configuration_rejects_an_own_scope_grant()
    {
        var service = Create(PermissionScope.Own, CurrentProfessionalResolution.Resolved(professionalId));

        var decision = await service.AuthorizeAllAsync("scheduling.configure");

        Assert.Equal(SchedulingScopeDecisionStatus.Forbidden, decision.Status);
        Assert.Equal(PermissionScope.Own, decision.Scope);
    }

    private SchedulingScopeAuthorization Create(PermissionScope scope, CurrentProfessionalResolution resolution) =>
        new(new StubUser(userId), new StubTenant(tenantId), new StubPermissions(scope), new StubProfessionalResolver(resolution));

    private sealed record StubUser(Guid Id) : ICurrentUser
    {
        public Guid? UserId => Id;
        public bool IsAuthenticated => true;
    }

    private sealed record StubTenant(Guid Id) : ICurrentTenant
    {
        public Guid? TenantId => Id;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }

    private sealed class StubPermissions(PermissionScope? scope) : IPermissionAuthorization
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode,
            CancellationToken cancellationToken = default) => Task.FromResult(scope is not null);

        public Task<PermissionScope?> GetPermissionScopeAsync(Guid userId, Guid tenantId, string permissionCode,
            CancellationToken cancellationToken = default) => Task.FromResult(scope);
    }

    private sealed class StubProfessionalResolver(CurrentProfessionalResolution result) : ICurrentProfessionalResolver
    {
        public int Calls { get; private set; }
        public Task<CurrentProfessionalResolution> ResolveAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
