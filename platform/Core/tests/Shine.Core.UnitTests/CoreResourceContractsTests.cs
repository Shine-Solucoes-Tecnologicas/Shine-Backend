using Shine.Domain;

namespace Shine.UnitTests;

public sealed class CoreResourceContractsTests
{
    [Fact]
    public void Resource_and_offering_descriptors_keep_tenant_and_module_boundaries()
    {
        var tenantId = Guid.NewGuid();
        var resource = new ResourceDescriptor(Guid.NewGuid(), tenantId, "Profissional", ModuleKey: "scheduling");
        var offering = new OfferingDescriptor(Guid.NewGuid(), tenantId, "Tarefa", ModuleKey: "tasks");
        var assignment = new ResourceAssignmentDescriptor(Guid.NewGuid(), tenantId, resource.Id, offering.Id);

        Assert.Equal(tenantId, resource.TenantId);
        Assert.Equal("scheduling", resource.ModuleKey);
        Assert.Equal("tasks", offering.ModuleKey);
        Assert.Equal(resource.Id, assignment.ResourceId);
        Assert.Equal(offering.Id, assignment.OfferingId);
    }

    [Fact]
    public void Policies_reject_unknown_conflict_modes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CapacityPolicy(1, (ConflictMode)99));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConflictPolicy((ConflictMode)99));
    }

    [Fact]
    public void Assignment_rules_reject_cross_tenant_links()
    {
        var tenant = Guid.NewGuid();
        ResourceAssignmentRules.Validate(tenant, Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<InvalidOperationException>(() => ResourceAssignmentRules.ValidateSameTenant(tenant, Guid.NewGuid(), tenant));
    }

    [Fact]
    public void Assignment_rules_reject_empty_identity()
    {
        Assert.Throws<ArgumentException>(() => ResourceAssignmentRules.Validate(Guid.Empty, Guid.NewGuid(), Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ResourceAssignmentRules.Validate(Guid.NewGuid(), Guid.Empty, Guid.NewGuid()));
        Assert.Throws<ArgumentException>(() => ResourceAssignmentRules.Validate(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty));
    }
}
