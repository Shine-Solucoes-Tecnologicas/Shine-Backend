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

    [Fact]
    public void Audit_entry_preserves_action_context_and_normalizes_time_to_utc()
    {
        var timestamp = new DateTime(2026, 8, 9, 10, 0, 0, DateTimeKind.Local);
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        var entry = AuditEntry.Create(
            "Tenant",
            tenantId.ToString(),
            "SUSPEND",
            userId,
            tenantId,
            timestamp,
            "trace-123",
            oldValuesJson: "{\"active\":true}",
            newValuesJson: "{\"active\":false}");

        Assert.Equal("Tenant", entry.EntityType);
        Assert.Equal("SUSPEND", entry.Action);
        Assert.Equal(userId, entry.UserId);
        Assert.Equal(tenantId, entry.TenantId);
        Assert.Equal("trace-123", entry.CorrelationId);
        Assert.Equal(DateTimeKind.Utc, entry.OccurredAtUtc.Kind);
        Assert.False(entry.IsSystemOperation);
    }

    [Fact]
    public void System_audit_entry_is_marked_when_no_user_is_available()
    {
        var entry = AuditEntry.Create("Migration", "1", "CREATE", null, null, DateTime.UtcNow);

        Assert.True(entry.IsSystemOperation);
    }

    private sealed class TestEntity : AuditableEntity;
}
