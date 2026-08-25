using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class BusinessCatalogPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Catalog_preserves_identifiers_and_filters_entries_by_unit()
    {
        var unitA = Guid.NewGuid();
        var unitB = Guid.NewGuid();
        var professional = new Professional(unitA, "Ana");
        var service = new Service(unitA, "Corte", 30);

        await using (var seed = fixture.CreateBusinessCatalogDb())
        {
            seed.AddRange(professional, service, new ProfessionalService(unitA, professional.Id, service.Id),
                new Professional(unitB, "Outro profissional"), new Service(unitB, "Outro serviço", 45));
            await seed.SaveChangesAsync();
        }

        await using var scoped = fixture.CreateBusinessCatalogDb(new TestTenant(unitA));
        Assert.Equal(professional.Id, Assert.Single(await scoped.Professionals.ToArrayAsync()).Id);
        Assert.Equal(service.Id, Assert.Single(await scoped.Services.ToArrayAsync()).Id);
        Assert.Single(await scoped.ProfessionalServices.ToArrayAsync());
    }

    [Fact]
    public async Task Catalog_rejects_cross_unit_and_unscoped_writes()
    {
        var unitA = Guid.NewGuid();
        var unitB = Guid.NewGuid();

        await using (var scoped = fixture.CreateBusinessCatalogDb(new TestTenant(unitA)))
        {
            scoped.Professionals.Add(new Professional(unitB, "Foreign"));
            await Assert.ThrowsAsync<TenantIsolationException>(() => scoped.SaveChangesAsync());
        }

        await using var unscoped = fixture.CreateUnscopedBusinessCatalogDb();
        unscoped.Services.Add(new Service(unitA, "Denied", 30));
        await Assert.ThrowsAsync<TenantIsolationException>(() => unscoped.SaveChangesAsync());
    }

    private sealed record TestTenant(Guid UnitId) : ICurrentTenant
    {
        public Guid? TenantId => UnitId;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }
}
