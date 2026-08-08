using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class PasswordHashServiceTests
{
    private readonly Pbkdf2PasswordHashService service = new();

    [Fact]
    public void Hashes_are_unique_and_verify_correct_password()
    {
        var first = service.Hash("Strong.Password1!");
        var second = service.Hash("Strong.Password1!");

        Assert.NotEqual(first, second);
        Assert.True(service.Verify("Strong.Password1!", first));
        Assert.False(service.Verify("Wrong.Password1!", first));
    }
}
