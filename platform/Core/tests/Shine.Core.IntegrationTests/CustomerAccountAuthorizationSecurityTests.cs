using Microsoft.EntityFrameworkCore;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class CustomerAccountAuthorizationSecurityTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Viewer_access_is_limited_to_assigned_units()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var viewer = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.ViewerName);
        var assignment = new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, viewer.Id);
        assignment.ReplaceScope(false, false, [setup.UnitA.Id], ["SCHEDULING"]);
        db.CustomerAccountUserRoles.Add(assignment);
        await db.SaveChangesAsync();
        var authorization = new CustomerAccountAuthorization(db);

        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.read", setup.UnitA.Id, "SCHEDULING"));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.read", setup.UnitB.Id, "SCHEDULING"));

        assignment.ReplaceScope(false, false, [setup.UnitA.Id, setup.UnitB.Id], ["SCHEDULING"]);
        await db.SaveChangesAsync();
        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.read", setup.UnitB.Id, "SCHEDULING"));
    }

    [Fact]
    public async Task Editor_module_scope_does_not_leak_to_other_modules()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var editor = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.EditorName);
        var assignment = new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, editor.Id);
        assignment.ReplaceScope(false, false, [setup.UnitA.Id], ["SCHEDULING"]);
        db.CustomerAccountUserRoles.Add(assignment);
        await db.SaveChangesAsync();
        var authorization = new CustomerAccountAuthorization(db);

        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.manage", setup.UnitA.Id, "SCHEDULING"));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.manage", setup.UnitA.Id, "CORE"));
    }

    [Fact]
    public async Task Accumulated_financial_and_editor_roles_keep_financial_and_operational_scopes_separate()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var roles = await db.CustomerAccountRoles.Where(x => x.AccountId == setup.Account.Id).ToDictionaryAsync(x => x.Name);
        var editor = new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, roles[CustomerAccountRole.EditorName].Id);
        editor.ReplaceScope(false, false, [setup.UnitA.Id], ["SCHEDULING"]);
        var financial = new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, roles[CustomerAccountRole.FinancialName].Id, allUnits: true, allModules: true);
        db.CustomerAccountUserRoles.AddRange(editor, financial);
        await db.SaveChangesAsync();
        var authorization = new CustomerAccountAuthorization(db);

        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "billing.manage", setup.UnitB.Id, "CORE"));
        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.manage", setup.UnitA.Id, "SCHEDULING"));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.manage", setup.UnitB.Id, "SCHEDULING"));
    }

    [Fact]
    public async Task Account_grant_never_authorizes_another_organization()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var otherAccount = new CustomerAccount($"Other {Guid.NewGuid():N}");
        var otherUnit = new Tenant($"Other unit {Guid.NewGuid():N}");
        otherUnit.AssignToCustomerAccount(otherAccount.Id);
        db.AddRange(otherAccount, otherUnit);
        var viewer = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.ViewerName);
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, viewer.Id, allUnits: true, allModules: true));
        await db.SaveChangesAsync();

        Assert.False(await new CustomerAccountAuthorization(db).HasPermissionAsync(setup.User.Id, otherAccount.Id, "scheduling.read", otherUnit.Id, "SCHEDULING"));
    }

    [Fact]
    public async Task Internal_platform_user_is_denied_even_when_an_account_role_exists()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        await AuthorizationSeed.EnsureGlobalRolesAsync(db);
        var platformAdmin = await db.GlobalRoles.SingleAsync(x => x.Name == GlobalRole.PlatformAdminName);
        db.UserGlobalRoles.Add(new UserGlobalRole(setup.User.Id, platformAdmin.Id));
        var viewer = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.ViewerName);
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, viewer.Id, allUnits: true, allModules: true));
        await db.SaveChangesAsync();

        Assert.False(await new CustomerAccountAuthorization(db).HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.read", setup.UnitA.Id, "SCHEDULING"));
    }

    [Fact]
    public async Task Operational_editor_has_no_financial_access()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var editor = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.EditorName);
        var assignment = new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, editor.Id, allUnits: true, allModules: true);
        db.CustomerAccountUserRoles.Add(assignment);
        await db.SaveChangesAsync();
        var authorization = new CustomerAccountAuthorization(db);

        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "scheduling.manage", setup.UnitA.Id, "SCHEDULING"));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "billing.read", setup.UnitA.Id));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "billing.manage", setup.UnitA.Id));
    }

    [Theory]
    [InlineData(CustomerAccountRole.FinancialName)]
    [InlineData(CustomerAccountRole.AdministratorName)]
    public async Task Customer_financial_roles_cannot_cross_organization_boundary(string roleName)
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var roles = await db.CustomerAccountRoles.Where(x => x.AccountId == setup.Account.Id).ToDictionaryAsync(x => x.Name);
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, roles[roleName].Id, true, true));
        var otherAccount = new CustomerAccount($"Other billing {Guid.NewGuid():N}");
        var otherUnit = new Tenant($"Other billing unit {Guid.NewGuid():N}");
        otherUnit.AssignToCustomerAccount(otherAccount.Id);
        db.AddRange(otherAccount, otherUnit);
        await db.SaveChangesAsync();
        var authorization = new CustomerAccountAuthorization(db);

        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "billing.manage", setup.UnitA.Id));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, otherAccount.Id, "billing.read", otherUnit.Id));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, otherAccount.Id, "billing.manage", otherUnit.Id));
    }

    [Fact]
    public async Task Missing_or_anonymous_grants_are_denied()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var accountAuthorization = new CustomerAccountAuthorization(db);
        var authorization = new PermissionAuthorization(db, accountAuthorization);

        Assert.False(await accountAuthorization.HasPermissionAsync(Guid.Empty, setup.Account.Id, "billing.read", setup.UnitA.Id));
        Assert.False(await accountAuthorization.HasPermissionAsync(setup.User.Id, setup.Account.Id, "billing.read", setup.UnitA.Id));
        Assert.False(await authorization.HasGlobalPermissionAsync(setup.User.Id, "billing.commercial.read"));
    }

    [Fact]
    public async Task Scheduling_system_roles_have_the_exact_grant_matrix_and_seed_is_idempotent()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);

        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, setup.Account.Id, setup.Owner.Id);

        var roles = await db.CustomerAccountRoles.AsNoTracking()
            .Where(x => x.AccountId == setup.Account.Id &&
                (x.Name == CustomerAccountRole.SchedulingProfessionalName ||
                 x.Name == CustomerAccountRole.SchedulingReceptionName ||
                 x.Name == CustomerAccountRole.SchedulingManagerName))
            .Select(x => new
            {
                x.Name,
                Grants = x.Permissions.Select(p => new { p.Permission.Code, p.Scope }).OrderBy(p => p.Code).ToArray()
            })
            .ToDictionaryAsync(x => x.Name);

        Assert.Equal(3, roles.Count);
        Assert.Collection(roles[CustomerAccountRole.SchedulingProfessionalName].Grants,
            grant => Assert.Equal(("scheduling.manage", PermissionScope.Own), (grant.Code, grant.Scope)),
            grant => Assert.Equal(("scheduling.read", PermissionScope.Own), (grant.Code, grant.Scope)));
        Assert.Collection(roles[CustomerAccountRole.SchedulingReceptionName].Grants,
            grant => Assert.Equal(("scheduling.manage", PermissionScope.All), (grant.Code, grant.Scope)),
            grant => Assert.Equal(("scheduling.read", PermissionScope.All), (grant.Code, grant.Scope)));
        Assert.Collection(roles[CustomerAccountRole.SchedulingManagerName].Grants,
            grant => Assert.Equal(("scheduling.configure", PermissionScope.All), (grant.Code, grant.Scope)),
            grant => Assert.Equal(("scheduling.manage", PermissionScope.All), (grant.Code, grant.Scope)),
            grant => Assert.Equal(("scheduling.read", PermissionScope.All), (grant.Code, grant.Scope)));
    }

    [Fact]
    public async Task All_scope_wins_over_own_and_revocation_is_visible_without_stale_cache()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var roles = await db.CustomerAccountRoles
            .Where(x => x.AccountId == setup.Account.Id)
            .ToDictionaryAsync(x => x.Name);
        var professional = new CustomerAccountUserRole(setup.Account.Id, setup.User.Id,
            roles[CustomerAccountRole.SchedulingProfessionalName].Id, allUnits: true, allModules: true);
        var reception = new CustomerAccountUserRole(setup.Account.Id, setup.User.Id,
            roles[CustomerAccountRole.SchedulingReceptionName].Id, allUnits: true, allModules: true);
        db.CustomerAccountUserRoles.AddRange(professional, reception);
        await db.SaveChangesAsync();
        var authorization = new CustomerAccountAuthorization(db);

        Assert.Equal(PermissionScope.All, await authorization.GetPermissionScopeAsync(
            setup.User.Id, setup.Account.Id, "scheduling.read", setup.UnitA.Id, "SCHEDULING"));

        await db.CustomerAccountUserRoles
            .Where(x => x.AccountId == setup.Account.Id && x.UserId == setup.User.Id &&
                x.RoleId == roles[CustomerAccountRole.SchedulingReceptionName].Id)
            .ExecuteDeleteAsync();

        var remainingGrants = await db.CustomerAccountUserRoles.AsNoTracking()
            .Where(x => x.AccountId == setup.Account.Id && x.UserId == setup.User.Id)
            .SelectMany(x => x.Role.Permissions
                .Where(p => p.Permission.Code == "scheduling.read")
                .Select(p => new { x.Role.Name, p.Scope }))
            .ToArrayAsync();
        var remainingGrant = Assert.Single(remainingGrants);
        Assert.Equal(CustomerAccountRole.SchedulingProfessionalName, remainingGrant.Name);
        Assert.Equal(PermissionScope.Own, remainingGrant.Scope);

        Assert.Equal(PermissionScope.Own, await authorization.GetPermissionScopeAsync(
            setup.User.Id, setup.Account.Id, "scheduling.read", setup.UnitA.Id, "SCHEDULING"));
    }

    [Fact]
    public async Task Account_administrator_and_custom_manage_grant_do_not_imply_scheduling_configuration()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateSetupAsync(db);
        var roles = await db.CustomerAccountRoles.Where(x => x.AccountId == setup.Account.Id).ToDictionaryAsync(x => x.Name);
        var administrator = roles[CustomerAccountRole.AdministratorName];
        var custom = new CustomerAccountRole(setup.Account.Id, $"Custom {Guid.NewGuid():N}");
        db.CustomerAccountRoles.Add(custom);
        var manage = await db.Permissions.SingleAsync(x => x.Code == "scheduling.manage");
        db.CustomerAccountRolePermissions.Add(new CustomerAccountRolePermission(custom.Id, manage.Id, PermissionScope.All));
        db.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(setup.Account.Id, setup.User.Id, custom.Id, true, true));
        await db.SaveChangesAsync();
        var authorization = new CustomerAccountAuthorization(db);

        Assert.False(await authorization.HasPermissionAsync(setup.Owner.Id, setup.Account.Id,
            "scheduling.read", setup.UnitA.Id, "SCHEDULING"));
        Assert.False(await authorization.HasPermissionAsync(setup.Owner.Id, setup.Account.Id,
            "scheduling.configure", setup.UnitA.Id, "SCHEDULING"));
        Assert.True(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id,
            "scheduling.manage", setup.UnitA.Id, "SCHEDULING"));
        Assert.False(await authorization.HasPermissionAsync(setup.User.Id, setup.Account.Id,
            "scheduling.configure", setup.UnitA.Id, "SCHEDULING"));
        Assert.Equal(CustomerAccountRole.AdministratorName, administrator.Name);
    }

    private static async Task<Setup> CreateSetupAsync(Shine.Infrastructure.Persistence.ShineDbContext db)
    {
        var owner = new User($"owner-{Guid.NewGuid():N}@example.test", "hash");
        var user = new User($"security-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Security organization {Guid.NewGuid():N}");
        var unitA = new Tenant($"Security A {Guid.NewGuid():N}");
        var unitB = new Tenant($"Security B {Guid.NewGuid():N}");
        unitA.AssignToCustomerAccount(account.Id);
        unitB.AssignToCustomerAccount(account.Id);
        db.AddRange(owner, user, account, unitA, unitB, new CustomerAccountUser(account.Id, owner.Id), new CustomerAccountUser(account.Id, user.Id));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, owner.Id);
        return new Setup(owner, user, account, unitA, unitB);
    }

    private sealed record Setup(User Owner, User User, CustomerAccount Account, Tenant UnitA, Tenant UnitB);
}
