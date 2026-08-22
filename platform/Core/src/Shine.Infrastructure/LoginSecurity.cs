namespace Shine.Infrastructure;

public sealed class LoginSecurityOptions
{
    public int MaxFailedAttempts { get; init; } = 5;
    public int LockoutMinutes { get; init; } = 15;
}
