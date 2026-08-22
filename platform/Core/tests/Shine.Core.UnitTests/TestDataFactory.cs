using Shine.Domain.Identity;

namespace Shine.UnitTests;

public static class TestDataFactory
{
    public static Tenant Tenant(string name = "Test tenant") => new(name);

    public static User User(string email = "user@example.com") => new(email, "hash");
}
