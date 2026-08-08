namespace Shine.Domain.Authorization;

using Shine.Domain.Identity;

public sealed class Permission
{
    private Permission() { }
    public Permission(string code, string description)
    {
        Id = Guid.NewGuid(); Code = code.Trim(); Description = description.Trim();
    }
    public Guid Id { get; private set; }
    public string Code { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public ICollection<RolePermission> Roles { get; private set; } = new List<RolePermission>();
}

public sealed class Role : IMultiTenantEntity
{
    public const string OwnerName = "Owner";
    public const string AdministratorName = "Administrator";
    private Role() { }
    public Role(Guid tenantId, string name, bool isSystem = false)
    {
        Id = Guid.NewGuid(); TenantId = tenantId; Name = name.Trim(); IsSystem = isSystem;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsSystem { get; private set; }
    public Tenant Tenant { get; private set; } = null!;
    public ICollection<RolePermission> Permissions { get; private set; } = new List<RolePermission>();
    public ICollection<UserTenantRole> Users { get; private set; } = new List<UserTenantRole>();
}

public sealed class RolePermission
{
    private RolePermission() { }
    public RolePermission(Guid roleId, Guid permissionId) { RoleId = roleId; PermissionId = permissionId; }
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }
    public Role Role { get; private set; } = null!;
    public Permission Permission { get; private set; } = null!;
}

public sealed class UserTenantRole : IMultiTenantEntity
{
    private UserTenantRole() { }
    public UserTenantRole(Guid userId, Guid tenantId, Guid roleId)
    { UserId = userId; TenantId = tenantId; RoleId = roleId; }
    public Guid UserId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RoleId { get; private set; }
    public User User { get; private set; } = null!;
    public UserTenant TenantMembership { get; private set; } = null!;
    public Role Role { get; private set; } = null!;
}
