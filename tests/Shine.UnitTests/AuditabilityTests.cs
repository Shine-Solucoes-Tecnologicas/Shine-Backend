using Shine.Domain;

namespace Shine.UnitTests;

public sealed class AuditabilityTests
{
    [Fact]
    public void MarkCreated_records_user_tenant_and_utc_time()
    {
        var entity = new TestEntity();
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Local);

        entity.MarkCreated(userId, tenantId, timestamp);

        Assert.Equal(userId, entity.CreatedByUserId);
        Assert.Equal(tenantId, entity.CreatedTenantId);
        Assert.Equal(DateTimeKind.Utc, entity.CreatedAtUtc.Kind);
        Assert.Null(entity.UpdatedAtUtc);
    }

    [Fact]
    public void MarkUpdated_requires_creation_first()
    {
        var entity = new TestEntity();

        Assert.Throws<DomainException>(() => entity.MarkUpdated(Guid.NewGuid(), DateTime.UtcNow));
    }

    private sealed class TestEntity : AuditableEntity;
}
