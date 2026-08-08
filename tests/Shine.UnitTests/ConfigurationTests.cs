using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class ConfigurationTests
{
    [Fact]
    public void Connection_string_is_required()
    {
        var options = new ConnectionStringOptions();
        Assert.Contains(nameof(ConnectionStringOptions.ShineDb),
            typeof(ConnectionStringOptions).GetProperties().Single().Name);
        Assert.True(string.IsNullOrWhiteSpace(options.ShineDb));
    }

    [Fact]
    public void File_storage_has_safe_defaults()
    {
        var options = new FileStorageOptions();
        Assert.True(options.MaxFileSizeBytes > 0);
        Assert.Contains(".pdf", options.AllowedExtensions);
        Assert.Contains("application/pdf", options.AllowedContentTypes[".pdf"]);
    }
}
