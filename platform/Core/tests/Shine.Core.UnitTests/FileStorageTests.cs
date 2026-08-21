using Microsoft.Extensions.Options;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class FileStorageTests
{
    [Fact]
    public async Task Saves_reads_and_deletes_a_valid_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "shine-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root }));
            await using var input = new MemoryStream("hello"u8.ToArray());

            var saved = await storage.SaveAsync(input, "document.pdf", "application/pdf");

            await using (var output = await storage.OpenReadAsync(saved.Id))
            {
                Assert.NotNull(output);
                using var reader = new StreamReader(output!);
                Assert.Equal("hello", await reader.ReadToEndAsync());
            }
            Assert.True(await storage.DeleteAsync(saved.Id));
            Assert.Null(await storage.OpenReadAsync(saved.Id));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("document.exe", "application/octet-stream")]
    [InlineData("document.pdf", "text/plain")]
    public async Task Rejects_invalid_extension_or_content_type(string name, string contentType)
    {
        var root = Path.Combine(Path.GetTempPath(), "shine-tests", Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root }));

        await using var input = new MemoryStream("data"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.SaveAsync(input, name, contentType));
    }
}
