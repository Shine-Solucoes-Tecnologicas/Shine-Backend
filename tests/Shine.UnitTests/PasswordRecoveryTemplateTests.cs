using Microsoft.Extensions.Configuration;
using Shine.Api;

namespace Shine.UnitTests;

public sealed class PasswordRecoveryTemplateTests
{
    [Fact]
    public void Template_contains_recovery_link_and_expiration_without_external_delivery()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["PasswordRecovery:BaseUrl"] = "https://shine.test" }).Build();
        var template = new PasswordRecoveryMessageTemplate(configuration);
        var expiry = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
        var message = template.Create("user@example.test", "raw-token", expiry);

        Assert.Contains("https://shine.test/reset-password?token=raw-token", message.TextBody);
        Assert.Contains("user@example.test", message.HtmlBody);
        Assert.Contains(expiry.ToString("O"), message.TextBody);
    }
}
