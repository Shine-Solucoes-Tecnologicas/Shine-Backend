using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class TenantExecutionContextTests
{
    [Fact]
    public void Requires_an_active_tenant_when_not_bypassing()
    {
        var context = new TenantExecutionContext(new FakeTenantContext(null, []));
        Assert.Throws<Shine.Domain.TenantIsolationException>(() => _ = context.TenantId);
    }

    [Fact]
    public void Admin_can_enter_a_scoped_bypass_and_state_is_restored()
    {
        var context = new TenantExecutionContext(new FakeTenantContext(Guid.NewGuid(), ["admin"]));

        using (context.EnterBypass())
            Assert.True(context.IsBypass);

        Assert.False(context.IsBypass);
    }

    [Fact]
    public void Non_admin_cannot_enter_a_bypass()
    {
        var context = new TenantExecutionContext(new FakeTenantContext(Guid.NewGuid(), ["user"]));
        Assert.Throws<Shine.Domain.TenantIsolationException>(() => context.EnterBypass());
        Assert.False(context.IsBypass);
    }

    private sealed class FakeTenantContext(Guid? tenantId, IReadOnlyCollection<string> roles) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
        public Guid? UserTenantId => null;
        public bool HasCompleteContext => TenantId is not null && UserTenantId is not null;
        public IReadOnlyCollection<string> Roles => roles;
    }
}
