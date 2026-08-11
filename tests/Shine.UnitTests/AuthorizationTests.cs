using Shine.Domain.Authorization;
using Shine.Domain.Identity;

namespace Shine.UnitTests;

public sealed class AuthorizationTests
{
    [Fact]
    public void Owner_role_is_tenant_scoped_and_system_defined()
    {
        var tenantId = Guid.NewGuid();
        var role = new Role(tenantId, Role.OwnerName, isSystem: true);

        Assert.Equal(tenantId, role.TenantId);
        Assert.Equal("Owner", role.Name);
        Assert.True(role.IsSystem);
    }

    [Fact]
    public void Owner_assignment_keeps_user_and_tenant_in_the_composite_key()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var assignment = new UserTenantRole(userId, tenantId, roleId);

        Assert.Equal(userId, assignment.UserId);
        Assert.Equal(tenantId, assignment.TenantId);
        Assert.Equal(roleId, assignment.RoleId);
    }

    [Fact]
    public void Administrator_role_has_a_distinct_system_name()
    {
        var role = new Role(Guid.NewGuid(), Role.AdministratorName, isSystem: true);

        Assert.Equal("Administrator", role.Name);
        Assert.True(role.IsSystem);
        Assert.NotEqual(Role.OwnerName, role.Name);
    }

    [Fact]
    public void Initial_permission_codes_are_unique_and_scoped_by_capability()
    {
        var permissions = Shine.Infrastructure.Persistence.Seed.AuthorizationSeed.InitialPermissions;

        Assert.Equal(permissions.Count, permissions.Keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains("tenant.read", permissions.Keys);
        Assert.Contains("users.manage", permissions.Keys);
        Assert.Contains("roles.manage", permissions.Keys);
        Assert.All(permissions.Keys, code => Assert.Contains('.', code));
    }

    [Fact]
    public void Tenant_seed_contract_has_owner_and_administrator_roles()
    {
        Assert.Equal("Owner", Role.OwnerName);
        Assert.Equal("Administrator", Role.AdministratorName);
        Assert.NotEqual(Role.OwnerName, Role.AdministratorName);
    }

    [Fact]
    public void Global_roles_have_the_expected_system_names()
    {
        Assert.Equal("PlatformAdmin", GlobalRole.PlatformAdminName);
        Assert.Equal("Support", GlobalRole.SupportName);
        Assert.Equal("Auditor", GlobalRole.AuditorName);
    }

    [Fact]
    public void Tenant_suspension_requires_a_reason_and_records_the_administrator()
    {
        var tenant = new Tenant("Acme");
        var administratorId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        tenant.Suspend("Fraud review", administratorId, now);

        Assert.False(tenant.IsActive);
        Assert.Equal("Fraud review", tenant.SuspensionReason);
        Assert.Equal(administratorId, tenant.SuspendedByUserId);
        Assert.Equal(now, tenant.SuspendedAtUtc);
        Assert.Throws<ArgumentException>(() => tenant.Suspend(" ", administratorId, now));
    }

    [Fact]
    public void Tenant_reactivation_clears_suspension_metadata()
    {
        var tenant = new Tenant("Acme");
        tenant.Suspend("Temporary", Guid.NewGuid(), DateTime.UtcNow);

        tenant.Reactivate();

        Assert.True(tenant.IsActive);
        Assert.Null(tenant.SuspensionReason);
        Assert.Null(tenant.SuspendedAtUtc);
        Assert.Null(tenant.SuspendedByUserId);
    }

    [Fact]
    public void User_block_and_unblock_change_account_status()
    {
        var user = new User("user@example.com", "hash");

        user.Block("Test block", Guid.NewGuid(), DateTime.UtcNow);
        Assert.False(user.IsActive);

        user.Unblock();
        Assert.True(user.IsActive);
    }
}
