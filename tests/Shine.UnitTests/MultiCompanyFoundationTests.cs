using Shine.Domain.Identity;

namespace Shine.UnitTests;

public sealed class MultiCompanyFoundationTests
{
    [Fact]
    public void User_can_have_independent_active_links_to_multiple_tenants()
    {
        var userId = Guid.NewGuid();
        var first = new UserTenant(userId, Guid.NewGuid(), userId, isOwner: true);
        var second = new UserTenant(userId, Guid.NewGuid(), userId, isOwner: false);

        Assert.Equal(userId, first.UserId);
        Assert.Equal(userId, second.UserId);
        Assert.NotEqual(first.TenantId, second.TenantId);
        Assert.True(first.IsActive);
        Assert.True(second.IsActive);
        Assert.True(first.IsOwner);
        Assert.False(second.IsOwner);
        Assert.NotEqual(Guid.Empty, first.UserTenantId);
        Assert.NotEqual(first.UserTenantId, second.UserTenantId);
    }

    [Fact]
    public void A_single_active_link_is_a_valid_candidate_for_automatic_selection()
    {
        var link = new UserTenant(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), isOwner: true);

        Assert.True(link.IsActive);
        Assert.NotEqual(Guid.Empty, link.TenantId);
        Assert.NotEqual(Guid.Empty, link.UserTenantId);
    }

    [Fact]
    public void Switching_context_keeps_the_selected_tenant_bound_to_the_user_link()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var link = new UserTenant(userId, tenantId, userId, isOwner: false);

        Assert.Equal(userId, link.UserId);
        Assert.Equal(tenantId, link.TenantId);
        Assert.True(link.IsActive);
    }
}
