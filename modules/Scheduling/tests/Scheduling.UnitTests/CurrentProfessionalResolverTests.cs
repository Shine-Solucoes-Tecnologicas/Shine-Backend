using BusinessCatalog.Application;
using Scheduling.Application;
using Scheduling.Infrastructure;
using Shine.Infrastructure;

namespace Scheduling.UnitTests;

public sealed class CurrentProfessionalResolverTests
{
    [Fact]
    public async Task Resolves_only_active_professional_linked_to_current_user_and_unit()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var professionalId = Guid.NewGuid();
        var catalog = new CatalogStub(new ProfessionalCatalogEntry(professionalId, "Ana", true));
        var resolver = new CurrentProfessionalResolver(new UserStub(userId), new TenantStub(tenantId), catalog);

        var result = await resolver.ResolveAsync();

        Assert.Equal(CurrentProfessionalResolutionStatus.Resolved, result.Status);
        Assert.Equal(professionalId, result.ProfessionalId);
        Assert.Equal((tenantId, userId), catalog.LastLookup);
    }

    [Theory]
    [InlineData(false, false, CurrentProfessionalResolutionStatus.MissingContext)]
    [InlineData(true, false, CurrentProfessionalResolutionStatus.MissingContext)]
    public async Task Missing_authentication_or_unit_context_returns_minimized_result(
        bool authenticated, bool completeTenant, CurrentProfessionalResolutionStatus expected)
    {
        var catalog = new CatalogStub(new ProfessionalCatalogEntry(Guid.NewGuid(), "Ana", true));
        var resolver = new CurrentProfessionalResolver(
            new UserStub(authenticated ? Guid.NewGuid() : null),
            new TenantStub(completeTenant ? Guid.NewGuid() : null),
            catalog);

        var result = await resolver.ResolveAsync();

        Assert.Equal(expected, result.Status);
        Assert.Null(result.ProfessionalId);
        Assert.Null(catalog.LastLookup);
    }

    [Theory]
    [InlineData(false, CurrentProfessionalResolutionStatus.Inactive)]
    public async Task Non_eligible_catalog_result_does_not_expose_professional_identifier(
        bool active, CurrentProfessionalResolutionStatus expected)
    {
        var catalog = new CatalogStub(new ProfessionalCatalogEntry(Guid.NewGuid(), "Ana", active));
        var resolver = new CurrentProfessionalResolver(new UserStub(Guid.NewGuid()), new TenantStub(Guid.NewGuid()), catalog);

        var result = await resolver.ResolveAsync();

        Assert.Equal(expected, result.Status);
        Assert.Null(result.ProfessionalId);
    }

    [Fact]
    public async Task Missing_link_returns_not_linked_without_identifier()
    {
        var resolver = new CurrentProfessionalResolver(
            new UserStub(Guid.NewGuid()), new TenantStub(Guid.NewGuid()), new CatalogStub(null));

        var result = await resolver.ResolveAsync();

        Assert.Equal(CurrentProfessionalResolutionStatus.NotLinked, result.Status);
        Assert.Null(result.ProfessionalId);
    }

    private sealed record UserStub(Guid? Id) : ICurrentUser
    {
        public Guid? UserId => Id;
        public bool IsAuthenticated => Id is not null;
    }

    private sealed record TenantStub(Guid? Id) : ICurrentTenant
    {
        public Guid? TenantId => Id;
        public Guid? UserTenantId => Id is null ? null : Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => Id is not null;
    }

    private sealed class CatalogStub(ProfessionalCatalogEntry? professional) : IBusinessCatalogReader
    {
        public (Guid TenantId, Guid UserId)? LastLookup { get; private set; }
        public Task<ProfessionalCatalogEntry?> FindProfessionalByUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken = default)
        {
            LastLookup = (tenantId, userId);
            return Task.FromResult(professional);
        }
        public Task<ProfessionalCatalogEntry?> FindProfessionalAsync(Guid tenantId, Guid professionalId, CancellationToken cancellationToken = default) => Task.FromResult<ProfessionalCatalogEntry?>(null);
        public Task<ServiceCatalogEntry?> FindServiceAsync(Guid tenantId, Guid serviceId, CancellationToken cancellationToken = default) => Task.FromResult<ServiceCatalogEntry?>(null);
        public Task<IReadOnlyCollection<ProfessionalCatalogEntry>> ListActiveProfessionalsAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ProfessionalCatalogEntry>>([]);
        public Task<IReadOnlyCollection<ProfessionalCatalogEntry>> FindProfessionalsAsync(Guid tenantId, IReadOnlyCollection<Guid> professionalIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ProfessionalCatalogEntry>>([]);
        public Task<IReadOnlyCollection<ServiceCatalogEntry>> FindServicesAsync(Guid tenantId, IReadOnlyCollection<Guid> serviceIds, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyCollection<ServiceCatalogEntry>>([]);
        public Task<bool> IsActiveAssociationAsync(Guid tenantId, Guid professionalId, Guid serviceId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
