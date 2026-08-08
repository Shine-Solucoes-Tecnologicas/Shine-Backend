using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class PasswordPolicyTests
{
    [Fact]
    public void Rejects_password_that_does_not_meet_policy()
    {
        var policy = new PasswordPolicy(Options.Create(new PasswordPolicyOptions()));

        Assert.False(policy.IsValid("weak", out var errors));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Accepts_password_that_meets_policy()
    {
        var policy = new PasswordPolicy(Options.Create(new PasswordPolicyOptions()));

        Assert.True(policy.IsValid("Strong.Password1!", out var errors));
        Assert.Empty(errors);
    }
}
