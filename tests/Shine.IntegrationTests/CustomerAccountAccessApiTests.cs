using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Api.Controllers;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class CustomerAccountAccessApiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Administrator_can_add_user_assign_scoped_role_and_revoke_it()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateOrganizationAsync(db);
        var controller = CreateController(db, setup.Actor.Id, setup.UnitA.Id);

        var added = await controller.AddUser(new AddCustomerAccountUserRequest(setup.Target.Email), default);
        Assert.IsType<CreatedResult>(added);

        var editor = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.EditorName);
        var assigned = await controller.Assign(setup.Target.Id, editor.Id,
            new AssignCustomerRoleRequest(false, false, [setup.UnitA.Id], ["SCHEDULING"]), default);
        Assert.IsType<NoContentResult>(assigned);
        Assert.True(await db.UserTenants.AnyAsync(x => x.UserId == setup.Target.Id && x.TenantId == setup.UnitA.Id && x.IsActive));
        Assert.True(await db.AuditEntries.AnyAsync(x => x.EntityType == "CustomerAccountAccess" && x.Action == "ROLE_ASSIGNED" && x.UserId == setup.Actor.Id));

        var revoked = await controller.Revoke(setup.Target.Id, editor.Id, default);
        Assert.IsType<NoContentResult>(revoked);
        Assert.False(await db.UserTenants.AnyAsync(x => x.UserId == setup.Target.Id && x.TenantId == setup.UnitA.Id && x.IsActive));
        Assert.True(await db.AuditEntries.AnyAsync(x => x.EntityType == "CustomerAccountAccess" && x.Action == "ROLE_REVOKED" && x.UserId == setup.Actor.Id));
    }

    [Fact]
    public async Task Administrator_cannot_grant_scope_outside_own_scope()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateOrganizationAsync(db);
        db.CustomerAccountUsers.Add(new CustomerAccountUser(setup.Account.Id, setup.Target.Id));
        await db.SaveChangesAsync();

        var administrator = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.AdministratorName);
        var actorAssignment = await db.CustomerAccountUserRoles.Include(x => x.Units).Include(x => x.Modules)
            .SingleAsync(x => x.AccountId == setup.Account.Id && x.UserId == setup.Actor.Id && x.RoleId == administrator.Id);
        actorAssignment.ReplaceScope(false, false, [setup.UnitA.Id], ["SCHEDULING"]);
        await db.SaveChangesAsync();

        var editor = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.EditorName);
        var controller = CreateController(db, setup.Actor.Id, setup.UnitA.Id);
        var result = await controller.Assign(setup.Target.Id, editor.Id,
            new AssignCustomerRoleRequest(false, false, [setup.UnitB.Id], ["SCHEDULING"]), default);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, error.StatusCode);
        Assert.Equal("SCOPE_ESCALATION_NOT_ALLOWED", Assert.IsType<AccountAccessError>(error.Value).Code);
    }

    [Fact]
    public async Task Last_administrator_cannot_be_revoked()
    {
        await using var db = fixture.CreateDb();
        var setup = await CreateOrganizationAsync(db);
        var administrator = await db.CustomerAccountRoles.SingleAsync(x => x.AccountId == setup.Account.Id && x.Name == CustomerAccountRole.AdministratorName);
        var controller = CreateController(db, setup.Actor.Id, setup.UnitA.Id);

        var result = await controller.Revoke(setup.Actor.Id, administrator.Id, default);

        var error = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, error.StatusCode);
        Assert.Equal("LAST_ADMINISTRATOR_REQUIRED", Assert.IsType<AccountAccessError>(error.Value).Code);
    }

    private static CustomerAccountAccessController CreateController(Shine.Infrastructure.Persistence.ShineDbContext db, Guid userId, Guid unitId)
    {
        var catalog = new ModuleCatalog();
        catalog.Register(ModuleDescriptor.Create("CORE", "Core", "Core platform"));
        catalog.Register(ModuleDescriptor.Create("SCHEDULING", "Scheduling", "Scheduling module", dependencies: ["CORE"]));
        return new CustomerAccountAccessController(db, new TestUser(userId), new TestTenant(unitId), new CustomerAccountAuthorization(db), catalog)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private static async Task<OrganizationSetup> CreateOrganizationAsync(Shine.Infrastructure.Persistence.ShineDbContext db)
    {
        var actor = new User($"actor-{Guid.NewGuid():N}@example.test", "hash");
        var target = new User($"target-{Guid.NewGuid():N}@example.test", "hash");
        var account = new CustomerAccount($"Organization {Guid.NewGuid():N}");
        var unitA = new Tenant($"Unit A {Guid.NewGuid():N}");
        var unitB = new Tenant($"Unit B {Guid.NewGuid():N}");
        unitA.AssignToCustomerAccount(account.Id);
        unitB.AssignToCustomerAccount(account.Id);
        db.AddRange(actor, target, account, unitA, unitB, new CustomerAccountUser(account.Id, actor.Id));
        db.UserTenants.Add(new UserTenant(actor.Id, unitA.Id, actor.Id, isOwner: true));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(db, account.Id, actor.Id);
        return new OrganizationSetup(actor, target, account, unitA, unitB);
    }

    private sealed record OrganizationSetup(User Actor, User Target, CustomerAccount Account, Tenant UnitA, Tenant UnitB);
    private sealed class TestUser(Guid userId) : ICurrentUser
    {
        public Guid? UserId => userId;
        public bool IsAuthenticated => true;
    }
    private sealed class TestTenant(Guid unitId) : ICurrentTenant
    {
        public Guid? TenantId => unitId;
        public Guid? UserTenantId => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }
}
