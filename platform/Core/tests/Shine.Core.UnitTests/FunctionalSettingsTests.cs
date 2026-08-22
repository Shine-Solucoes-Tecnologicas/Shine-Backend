using Shine.Domain;

namespace Shine.UnitTests;

public sealed class FunctionalSettingsTests
{
    [Fact]
    public void Normalizes_functional_keys()
    {
        var setting = new FunctionalSetting(" ui.theme ", "dark");

        Assert.Equal("UI.THEME", setting.Key);
        Assert.Equal("dark", setting.Value);
    }

    [Theory]
    [InlineData("JWT_SECRET")]
    [InlineData("database.connection_string")]
    [InlineData("admin.password")]
    public void Rejects_technical_secrets(string key)
    {
        Assert.Throws<ArgumentException>(() => new FunctionalSetting(key, "value"));
    }
}
