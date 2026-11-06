namespace Shine.Domain.Identity;

public sealed class User
{
    private User() { }

    public User(string email, string passwordHash, bool emailVerified = true)
    {
        Id = Guid.NewGuid();
        Email = email.Trim();
        NormalizedEmail = NormalizeEmail(email);
        PasswordHash = passwordHash;
        EmailVerifiedAtUtc = emailVerified ? DateTime.UtcNow : null;
        IsActive = true;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public DateTime? EmailVerifiedAtUtc { get; private set; }
    public bool IsEmailVerified => EmailVerifiedAtUtc.HasValue;
    public bool IsActive { get; private set; }
    public int FailedLoginAttempts { get; private set; }
    public DateTime? LockedUntilUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? BlockReason { get; private set; }
    public DateTime? BlockedAtUtc { get; private set; }
    public Guid? BlockedByUserId { get; private set; }
    public ICollection<UserTenant> Tenants { get; private set; } = new List<UserTenant>();

    public static string NormalizeEmail(string email) => email.Trim().ToUpperInvariant();

    public void ChangePassword(string passwordHash) => PasswordHash = passwordHash;
    public void VerifyEmail(DateTime nowUtc) => EmailVerifiedAtUtc ??= nowUtc;
    public void ChangeEmail(string email)
    {
        var normalized = NormalizeEmail(email);
        if (normalized == NormalizedEmail) return;
        Email = email.Trim();
        NormalizedEmail = normalized;
        EmailVerifiedAtUtc = null;
    }
    public void Block(string reason, Guid administratorUserId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A block reason is required.", nameof(reason));
        IsActive = false;
        BlockReason = reason.Trim();
        BlockedAtUtc = nowUtc;
        BlockedByUserId = administratorUserId;
    }
    public void Unblock()
    {
        IsActive = true;
        BlockReason = null;
        BlockedAtUtc = null;
        BlockedByUserId = null;
    }
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
