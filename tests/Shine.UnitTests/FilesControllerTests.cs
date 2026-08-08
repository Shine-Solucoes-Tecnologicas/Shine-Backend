using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Shine.Api.Controllers;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class FilesControllerTests
{
    [Fact]
    public async Task Upload_returns_created_file_metadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "shine-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var controller = new FilesController(new LocalFileStorage(Options.Create(new FileStorageOptions { RootPath = root })));
            await using var stream = new MemoryStream("content"u8.ToArray());
            var file = new FormFile(stream, 0, stream.Length, "file", "document.pdf")
            {
                Headers = new HeaderDictionary(),
                ContentType = "application/pdf"
            };

            var result = await controller.Upload(file, CancellationToken.None);

            var created = Assert.IsType<CreatedAtActionResult>(result.Result);
            Assert.IsType<FileUploadResponse>(created.Value);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Upload_rejects_missing_file()
    {
        var controller = new FilesController(new LocalFileStorage(Options.Create(new FileStorageOptions())));
        var result = await controller.Upload(null, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
