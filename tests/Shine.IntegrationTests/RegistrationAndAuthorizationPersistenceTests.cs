using Microsoft.EntityFrameworkCore;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence.Seed;

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
}
