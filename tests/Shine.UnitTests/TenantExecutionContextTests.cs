using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class TenantExecutionContextTests
{
    [Fact]
    public void Explicit_tenant_scope_supports_anonymous_route_without_allowing_authenticated_switch()
    {
        var selected = Guid.NewGuid();
        var anonymous = new TenantExecutionContext(new FakeTenantContext(null, []));
        using (anonymous.EnterTenant(selected)) Assert.Equal(selected, anonymous.EffectiveTenantId);
        Assert.Null(anonymous.EffectiveTenantId);

        var authenticated = new TenantExecutionContext(new FakeTenantContext(Guid.NewGuid(), []));
        Assert.Throws<Shine.Domain.TenantIsolationException>(() => authenticated.EnterTenant(selected));
    }

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

    [Fact]
    public void Nested_bypass_restores_each_previous_state()
    {
        var context = new TenantExecutionContext(new FakeTenantContext(Guid.NewGuid(), ["admin"]));

        using (context.EnterBypass())
        {
            Assert.True(context.IsBypass);
            using (context.EnterBypass())
                Assert.True(context.IsBypass);

            Assert.True(context.IsBypass);
        }

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
