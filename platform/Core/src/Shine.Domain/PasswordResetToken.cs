namespace Shine.Domain.Identity;

public sealed class PasswordResetToken
{
    private PasswordResetToken() { }

    public PasswordResetToken(Guid userId, string tokenHash, DateTime expiresAtUtc)
    {
        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAtUtc = expiresAtUtc;
        CreatedAtUtc = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public User User { get; private set; } = null!;
    public string TokenHash { get; private set; } = null!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public bool IsValid(DateTime nowUtc) => UsedAtUtc is null && ExpiresAtUtc > nowUtc;
    public void MarkUsed(DateTime nowUtc) => UsedAtUtc = nowUtc;
}
