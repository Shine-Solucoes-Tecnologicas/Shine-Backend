namespace Shine.Domain;

/// <summary>Shared identity and lifecycle contract for anything that can be assigned work.</summary>
public interface IResource
{
    Guid Id { get; }
    Guid TenantId { get; }
    string Name { get; }
    bool IsActive { get; }
    string? ModuleKey { get; }
}

/// <summary>Shared contract for a bookable or deliverable offering.</summary>
public interface IOffering
{
    Guid Id { get; }
    Guid TenantId { get; }
    string Name { get; }
    bool IsActive { get; }
    string? ModuleKey { get; }
}

/// <summary>Associates a tenant resource with an offering without embedding module rules.</summary>
public interface IResourceAssignment
{
    Guid Id { get; }
    Guid TenantId { get; }
    Guid ResourceId { get; }
    Guid OfferingId { get; }
    bool IsActive { get; }
}

public sealed record ResourceDescriptor(Guid Id, Guid TenantId, string Name, bool IsActive = true, string? ModuleKey = null);

public sealed record OfferingDescriptor(Guid Id, Guid TenantId, string Name, bool IsActive = true, string? ModuleKey = null);

public sealed record ResourceAssignmentDescriptor(Guid Id, Guid TenantId, Guid ResourceId, Guid OfferingId, bool IsActive = true);
