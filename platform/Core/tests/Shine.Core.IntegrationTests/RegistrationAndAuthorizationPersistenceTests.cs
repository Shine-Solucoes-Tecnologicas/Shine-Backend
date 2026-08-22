using Microsoft.EntityFrameworkCore;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence.Seed;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class RegistrationAndAuthorizationPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Tenant_registration_persists_owner_and_administrator_roles()
    {
        var user = new User($"owner-{Guid.NewGuid():N}@example.test", "hash");
        var tenant = new Tenant($"Tenant {Guid.NewGuid():N}");
        fixture.Db.Users.Add(user);
        fixture.Db.Tenants.Add(tenant);
        fixture.Db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, user.Id, isOwner: true));
        await fixture.Db.SaveChangesAsync();

        await AuthorizationSeed.SeedTenantDefaultsAsync(fixture.Db, tenant.Id, user.Id, user.Id);

        var roleNames = await fixture.Db.UserTenantRoles
            .Where(link => link.UserId == user.Id && link.TenantId == tenant.Id)
            .Select(link => link.Role.Name)
            .ToArrayAsync();

        Assert.Contains(Role.OwnerName, roleNames);
        Assert.Contains(Role.AdministratorName, roleNames);
    }

    [Fact]
    public async Task User_tenant_role_assignment_is_scoped_to_the_same_tenant()
    {
        var user = new User($"member-{Guid.NewGuid():N}@example.test", "hash");
        var tenantA = new Tenant($"Tenant {Guid.NewGuid():N}");
        var tenantB = new Tenant($"Tenant {Guid.NewGuid():N}");
        fixture.Db.Users.Add(user);
        fixture.Db.Tenants.AddRange(tenantA, tenantB);
        fixture.Db.UserTenants.Add(new UserTenant(user.Id, tenantA.Id, user.Id, isOwner: false));
        await fixture.Db.SaveChangesAsync();
        await AuthorizationSeed.SeedTenantDefaultsAsync(fixture.Db, tenantA.Id, user.Id, user.Id);

        var rolesInOtherTenant = await fixture.Db.UserTenantRoles
            .Where(link => link.UserId == user.Id && link.TenantId == tenantB.Id)
            .AnyAsync();

        Assert.False(rolesInOtherTenant);
    }

    [Fact]
    public async Task Global_role_seed_creates_platform_roles_and_permissions()
    {
        await AuthorizationSeed.EnsureGlobalRolesAsync(fixture.Db);

        var roles = await fixture.Db.GlobalRoles
            .Where(role => role.Name == GlobalRole.PlatformAdminName)
            .Select(role => new { role.Name, Permissions = role.Permissions.Select(link => link.Permission.Code).ToArray() })
            .SingleAsync();

        Assert.Equal(GlobalRole.PlatformAdminName, roles.Name);
        Assert.Contains("admin.read", roles.Permissions);
        Assert.Contains("admin.manage", roles.Permissions);
        Assert.Contains("admin.audit", roles.Permissions);
    }

    [Fact]
    public async Task Commercial_manager_has_commercial_access_without_operational_or_platform_management_permissions()
    {
        await using var db = fixture.CreateDb();
        await AuthorizationSeed.EnsureGlobalRolesAsync(db);
        var user = new User($"commercial-{Guid.NewGuid():N}@example.test", "hash");
        var role = await db.GlobalRoles.SingleAsync(x => x.Name == GlobalRole.CommercialManagerName);
        db.Users.Add(user);
        db.UserGlobalRoles.Add(new UserGlobalRole(user.Id, role.Id));
        await db.SaveChangesAsync();
        var authorization = new PermissionAuthorization(db, new CustomerAccountAuthorization(db));

        Assert.True(await authorization.HasGlobalPermissionAsync(user.Id, "billing.commercial.read"));
        Assert.True(await authorization.HasGlobalPermissionAsync(user.Id, "billing.commercial.manage"));
        Assert.False(await authorization.HasGlobalPermissionAsync(user.Id, "admin.manage"));
        Assert.False(await authorization.HasPermissionAsync(user.Id, Guid.NewGuid(), "scheduling.manage"));
    }

    [Fact]
    public async Task Customer_roles_are_accumulative_and_financial_access_does_not_grant_scheduling()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"account-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Account {Guid.NewGuid():N}");
        var tenant = new Tenant($"Unit {Guid.NewGuid():N}");
        tenant.AssignToCustomerAccount(account.Id);
        db.AddRange(user, account, tenant, new UserTenant(user.Id, tenant.Id, user.Id, false),
            new CustomerAccountUser(account.Id, user.Id));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, user.Id);

        var accountAuthorization = new CustomerAccountAuthorization(db);
        var authorization = new PermissionAuthorization(db, accountAuthorization);
        Assert.True(await authorization.HasPermissionAsync(user.Id, tenant.Id, "billing.read"));
        Assert.False(await authorization.HasPermissionAsync(user.Id, tenant.Id, "scheduling.read"));

        var editor = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == account.Id && x.Name == CustomerAccountRole.EditorName);
        var assignment = new CustomerAccountUserRole(account.Id, user.Id, editor.Id);
        assignment.ReplaceScope(false, false, [tenant.Id], ["SCHEDULING"]);
        db.CustomerAccountUserRoles.Add(assignment);
        await db.SaveChangesAsync();

        Assert.True(await authorization.HasPermissionAsync(user.Id, tenant.Id, "scheduling.read"));
    }

    [Fact]
    public async Task Inactive_unit_membership_revokes_customer_account_permissions_immediately()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"revoked-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Revoked account {Guid.NewGuid():N}");
        var tenant = new Tenant($"Revoked unit {Guid.NewGuid():N}");
        tenant.AssignToCustomerAccount(account.Id);
        var membership = new UserTenant(user.Id, tenant.Id, user.Id, false);
        db.AddRange(user, account, tenant, membership, new CustomerAccountUser(account.Id, user.Id));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, user.Id);

        var authorization = new PermissionAuthorization(db, new CustomerAccountAuthorization(db));
        Assert.True(await authorization.HasPermissionAsync(user.Id, tenant.Id, "billing.read"));

        membership.Deactivate();
        await db.SaveChangesAsync();

        Assert.False(await authorization.HasPermissionAsync(user.Id, tenant.Id, "billing.read"));
    }
}
