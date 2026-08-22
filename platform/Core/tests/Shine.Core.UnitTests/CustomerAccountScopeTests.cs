using Shine.Domain.Identity;

namespace Shine.UnitTests;

public sealed class CustomerAccountScopeTests
{
    [Fact]
    public void MultipleAssignmentsCanCoverDifferentDimensions()
    {
        var accountId = Guid.NewGuid(); var userId = Guid.NewGuid(); var roleId = Guid.NewGuid(); var unitId = Guid.NewGuid();
        var unitAssignment = new CustomerAccountUserRole(accountId, userId, roleId, allUnits: false, allModules: true);
        unitAssignment.Units.Add(new CustomerAccountUserRoleUnit(accountId, userId, roleId, unitId));
        var broadAssignment = new CustomerAccountUserRole(accountId, userId, Guid.NewGuid(), allUnits: true, allModules: true);
        Assert.Contains(unitAssignment.Units, x => x.UnitId == unitId);
        Assert.True(broadAssignment.AllUnits && broadAssignment.AllModules);
    }

    [Fact]
    public void ModuleCodesAreNormalizedInScopes()
    {
        var scope = new CustomerAccountUserRoleModule(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), " scheduling ");
        Assert.Equal("SCHEDULING", scope.ModuleCode);
    }
}
