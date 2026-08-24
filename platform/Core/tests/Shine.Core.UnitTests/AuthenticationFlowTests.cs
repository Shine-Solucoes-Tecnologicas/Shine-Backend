using Microsoft.Extensions.Options;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class AuthenticationFlowTests
{
    [Fact]
    public void Refresh_token_is_active_until_expiry_or_revocation()
    {
        var token = new RefreshToken(Guid.NewGuid(), null, null, "hash", DateTime.UtcNow.AddMinutes(5));

        Assert.True(token.IsActive(DateTime.UtcNow));
        token.Revoke(DateTime.UtcNow, "replacement");
        Assert.False(token.IsActive(DateTime.UtcNow));
        Assert.Equal("replacement", token.ReplacedByTokenHash);
    }

    [Fact]
    public void Password_hash_round_trip_accepts_only_the_original_password()
    {
        var service = new Pbkdf2PasswordHashService();
        var encoded = service.Hash("Strong-password-123!");

        Assert.True(service.Verify("Strong-password-123!", encoded));
        Assert.False(service.Verify("wrong-password", encoded));
    }

    [Fact]
    public void Password_policy_reports_all_missing_requirements()
    {
        var policy = new PasswordPolicy(Options.Create(new PasswordPolicyOptions()));

        var valid = policy.IsValid("Strong-password-123!", out var errors);
        var invalid = policy.IsValid("weak", out var invalidErrors);

        Assert.True(valid);
        Assert.Empty(errors);
        Assert.False(invalid);
        Assert.NotEmpty(invalidErrors);
    }

    [Fact]
    public void User_is_temporarily_locked_after_repeated_failed_logins()
    {
        var user = new User("locked@example.test", "hash");
        var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);

        for (var attempt = 0; attempt < 5; attempt++) user.RegisterFailedLogin(now, 5, TimeSpan.FromMinutes(15));

        Assert.True(user.IsLoginLocked(now));
        Assert.False(user.IsLoginLocked(now.AddMinutes(16)));
    }

    [Fact]
    public void Registration_can_create_an_unverified_user_and_email_change_requires_verification_again()
    {
        var user = new User("pending@example.test", "hash", emailVerified: false);
        Assert.False(user.IsEmailVerified);

        user.VerifyEmail(DateTime.UtcNow);
        Assert.True(user.IsEmailVerified);

        user.ChangeEmail("new-address@example.test");
        Assert.Equal("NEW-ADDRESS@EXAMPLE.TEST", user.NormalizedEmail);
        Assert.False(user.IsEmailVerified);
    }

    [Fact]
    public void Email_verification_token_expires_and_can_only_be_consumed_once()
    {
        var now = DateTime.UtcNow;
        var token = new EmailVerificationToken(Guid.NewGuid(), "hash", now.AddMinutes(30), now);

        Assert.True(token.IsValid(now));
        token.MarkUsed(now.AddMinutes(1));
        Assert.False(token.IsValid(now.AddMinutes(1)));

        var expired = new EmailVerificationToken(Guid.NewGuid(), "expired", now.AddSeconds(-1), now.AddHours(-1));
        Assert.False(expired.IsValid(now));
    }
}
