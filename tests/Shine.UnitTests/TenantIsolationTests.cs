using Shine.Domain;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class TenantIsolationTests
{
    [Fact]
    public void ForTenant_filters_entities_to_the_active_tenant()
    {
        var tenantId = Guid.NewGuid();
        var otherTenantId = Guid.NewGuid();
        var entities = new[]
        {
            new TenantEntity(tenantId),
            new TenantEntity(otherTenantId),
            new TenantEntity(tenantId)
        }.AsQueryable();

        var result = entities.ForTenant(tenantId).ToArray();

        Assert.Equal(2, result.Length);
        Assert.All(result, entity => Assert.Equal(tenantId, entity.TenantId));
    }

    [Fact]
    public void EnsureTenant_rejects_an_entity_from_another_tenant()
    {
        var entity = new TenantEntity(Guid.NewGuid());

        Assert.Throws<TenantIsolationException>(() => entity.EnsureTenant(Guid.NewGuid()));
    }

    private sealed record TenantEntity(Guid TenantId) : IMultiTenantEntity;
}
