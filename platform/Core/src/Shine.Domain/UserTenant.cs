namespace Shine.Domain.Identity;

public sealed class UserTenant
{
    private UserTenant() { }

    public UserTenant(Guid userId, Guid tenantId, Guid createdByUserId, bool isOwner)
    {
        UserTenantId = Guid.NewGuid();
        UserId = userId;
        TenantId = tenantId;
        CreatedByUserId = createdByUserId;
        IsOwner = isOwner;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid UserTenantId { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public Guid TenantId { get; private set; }
    public Tenant Tenant { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public bool IsOwner { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
