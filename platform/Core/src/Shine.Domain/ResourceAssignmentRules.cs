namespace Shine.Domain;

public static class ResourceAssignmentRules
{
    public static void Validate(Guid tenantId, Guid resourceId, Guid offeringId, Guid? assignmentId = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (resourceId == Guid.Empty) throw new ArgumentException("Resource is required.", nameof(resourceId));
        if (offeringId == Guid.Empty) throw new ArgumentException("Offering is required.", nameof(offeringId));
        if (assignmentId is Guid id && id == Guid.Empty) throw new ArgumentException("Assignment is invalid.", nameof(assignmentId));
    }

    public static void ValidateSameTenant(Guid resourceTenantId, Guid offeringTenantId, Guid assignmentTenantId)
    {
        if (resourceTenantId != offeringTenantId || resourceTenantId != assignmentTenantId)
            throw new InvalidOperationException("Resource, offering and assignment must belong to the same tenant.");
    }
}
