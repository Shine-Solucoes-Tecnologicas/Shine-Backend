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
    public ICollection<UserTenant> Users { get; private set; } = new List<UserTenant>();
}
