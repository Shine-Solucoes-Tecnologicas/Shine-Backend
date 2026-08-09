namespace Shine.Domain.Identity;

public sealed class Tenant
{
    private Tenant() { }

    public Tenant(string name)
    {
        Id = Guid.NewGuid();
        Name = name.Trim();
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public Guid? SuspendedByUserId { get; private set; }
    public ICollection<UserTenant> Users { get; private set; } = new List<UserTenant>();

    public void Suspend(string reason, Guid administratorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A suspension reason is required.", nameof(reason));
        IsActive = false;
        SuspensionReason = reason.Trim();
        SuspendedAtUtc = nowUtc;
        SuspendedByUserId = administratorUserId;
    }

    public void Reactivate()
    {
        IsActive = true;
        SuspensionReason = null;
        SuspendedAtUtc = null;
        SuspendedByUserId = null;
    }
}
