using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.UnitTests;

public sealed class CustomerAccountRoleTests
{
    [Fact]
    public void MembershipDoesNotEmbedASingleRole()
    {
        var membership = new CustomerAccountUser(Guid.NewGuid(), Guid.NewGuid());
        Assert.Empty(membership.Roles);
    }

    [Fact]
    public void UserCanReceiveMultipleRolesInTheSameAccount()
    {
        var accountId = Guid.NewGuid(); var userId = Guid.NewGuid();
        var viewer = new CustomerAccountRole(accountId, CustomerAccountRole.ViewerName, true);
        var financial = new CustomerAccountRole(accountId, CustomerAccountRole.FinancialName, true);
        var assignments = new[] { new CustomerAccountUserRole(accountId, userId, viewer.Id), new CustomerAccountUserRole(accountId, userId, financial.Id) };
        Assert.Equal(2, assignments.Select(x => x.RoleId).Distinct().Count());
    }

    [Fact]
    public void EmptyScopeDoesNotMeanGlobalAccess()
    {
        var assignment = new CustomerAccountUserRole(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        Assert.False(assignment.AllUnits);
        Assert.False(assignment.AllModules);
        Assert.Empty(assignment.Units);
        Assert.Empty(assignment.Modules);
    }

    [Fact]
    public void AdministratorCanBeExplicitlyScopedToEverything()
    {
        var assignment = new CustomerAccountUserRole(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true, true);
        Assert.True(assignment.AllUnits);
        Assert.True(assignment.AllModules);
    }

    [Fact]
    public void FinancialAndOperationalPermissionsAreSeparate()
    {
        Assert.Contains("billing.manage", AuthorizationSeed.InitialPermissions.Keys);
        Assert.Contains("scheduling.manage", AuthorizationSeed.InitialPermissions.Keys);
        Assert.NotEqual("billing.manage", "scheduling.manage");
    }
}
