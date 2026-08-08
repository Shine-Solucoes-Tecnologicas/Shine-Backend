using Microsoft.Extensions.Options;

namespace Shine.Infrastructure;

public sealed class FileStorageOptions
{
    public string RootPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "storage");
    public long MaxFileSizeBytes { get; init; } = 10 * 1024 * 1024;
    public string[] AllowedExtensions { get; init; } = [".pdf", ".png", ".jpg", ".jpeg"];
    public Dictionary<string, string[]> AllowedContentTypes { get; init; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = ["application/pdf"],
        [".png"] = ["image/png"],
        [".jpg"] = ["image/jpeg"],
        [".jpeg"] = ["image/jpeg"]
    };
}

public sealed record StoredFile(string Id, string OriginalFileName, string ContentType, long Length);

public interface IFileStorage
{
    Task<StoredFile> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private readonly FileStorageOptions settings = options.Value;

    public async Task<StoredFile> SaveAsync(Stream content, string originalFileName, string contentType, CancellationToken cancellationToken = default)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!settings.AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("File extension is not allowed.");
        if (!settings.AllowedContentTypes.TryGetValue(extension, out var contentTypes) ||
            !contentTypes.Contains(contentType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("File content type is not allowed for the extension.");
        if (content.Length > settings.MaxFileSizeBytes)
            throw new InvalidDataException("File exceeds the configured size limit.");

        Directory.CreateDirectory(settings.RootPath);
        var id = Guid.NewGuid().ToString("N");
        var path = GetPath(id);
        await using var target = File.Create(path);
        await content.CopyToAsync(target, cancellationToken);
        return new StoredFile(id, Path.GetFileName(originalFileName), contentType, content.Length);
    }

    public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = GetPath(id);
        if (!File.Exists(path)) return Task.FromResult<Stream?>(null);
        return Task.FromResult<Stream?>(File.OpenRead(path));
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var path = GetPath(id);
        if (!File.Exists(path)) return Task.FromResult(false);
        File.Delete(path);
        return Task.FromResult(true);
    }

    private string GetPath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("Invalid file identifier.", nameof(id));
        return Path.Combine(settings.RootPath, id);
    }
}
