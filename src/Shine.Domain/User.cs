namespace Shine.Domain.Identity;

public sealed class User
{
    private User() { }

    public User(string email, string passwordHash)
    {
        Id = Guid.NewGuid();
        Email = email.Trim();
        NormalizedEmail = NormalizeEmail(email);
        PasswordHash = passwordHash;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<UserTenant> Tenants { get; private set; } = new List<UserTenant>();

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;
}
