namespace Shine.Domain.Identity;

public sealed class CustomerAccount
{
    private CustomerAccount() { }
    public CustomerAccount(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Account name is required.", nameof(name));
        Id = Guid.NewGuid(); Name = name.Trim(); IsActive = true; CreatedAtUtc = DateTime.UtcNow;
    }
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<Tenant> Tenants { get; private set; } = new List<Tenant>();
}
