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
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public ICollection<UserTenant> Tenants { get; private set; } = new List<UserTenant>();

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;
    public void Block() => IsActive = false;
    public void Unblock() => IsActive = true;
    public bool IsLoginLocked(DateTime nowUtc) => LockedUntilUtc is DateTime lockedUntil && lockedUntil > nowUtc;
    public void RegisterFailedLogin(DateTime nowUtc, int maxAttempts, TimeSpan lockout)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
        {
            LockedUntilUtc = nowUtc.Add(lockout);
            FailedLoginAttempts = 0;
        }
    }
    public void RegisterSuccessfulLogin()
    {
        FailedLoginAttempts = 0;
        LockedUntilUtc = null;
    }
}
