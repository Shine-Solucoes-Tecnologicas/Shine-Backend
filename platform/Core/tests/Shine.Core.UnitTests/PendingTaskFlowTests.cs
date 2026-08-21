using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class PendingTaskFlowTests
{
    [Fact]
    public void UserTenant_can_be_deactivated_and_reactivated_without_changing_identity()
    {
        var link = new UserTenant(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), isOwner: false);
        var id = link.UserTenantId;

        link.Deactivate();
        link.Reactivate();

        Assert.Equal(id, link.UserTenantId);
        Assert.True(link.IsActive);
    }

    [Fact]
    public void Module_access_normalizes_code_and_can_be_disabled()
    {
        var access = new ModuleAccess(Guid.NewGuid(), " core-module ");

        access.SetEnabled(false);

        Assert.Equal("CORE-MODULE", access.ModuleCode);
        Assert.False(access.Enabled);
    }

    [Fact]
    public void Refresh_token_factory_returns_raw_token_and_only_hash_is_persisted()
    {
        var (raw, hash) = RefreshTokenHash.Create();

        Assert.NotEmpty(raw);
        Assert.NotEmpty(hash);
        Assert.Equal(hash, RefreshTokenHash.Hash(raw));
        Assert.NotEqual(raw, hash);
    }

    [Fact]
    public void Auditable_entity_supports_soft_delete_and_restore()
    {
        var entity = new ModuleAccess(Guid.NewGuid(), "CORE");
        var deletedAt = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

        entity.Delete(deletedAt);
        Assert.True(entity.IsDeleted);
        Assert.Equal(deletedAt, entity.DeletedAtUtc);

        entity.Restore();
        Assert.False(entity.IsDeleted);
        Assert.Null(entity.DeletedAtUtc);
    }
}
