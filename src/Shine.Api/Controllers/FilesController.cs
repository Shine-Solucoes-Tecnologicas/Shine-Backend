using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Shine.Infrastructure;
using Shine.Api;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
[RequiresModule("CORE")]
public sealed class FilesController(IFileStorage storage) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(10 * 1024 * 1024)]
    [ProducesResponseType<FileUploadResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<FileUploadResponse>> Upload(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
            return BadRequest(new { code = "file_required", message = "A non-empty file is required." });

        await using var content = file.OpenReadStream();
        try
        {
            var stored = await storage.SaveAsync(content, file.FileName, file.ContentType, cancellationToken);
            var response = new FileUploadResponse(stored.Id, stored.OriginalFileName, stored.ContentType, stored.Length);
            return CreatedAtAction(nameof(Download), new { id = stored.Id }, response);
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { code = "invalid_file", message = exception.Message });
        }
    }

    [HttpGet("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string id, CancellationToken cancellationToken)
    {
        try
        {
            var content = await storage.OpenReadAsync(id, cancellationToken);
            return content is null ? NotFound() : File(content, "application/octet-stream", enableRangeProcessing: true);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            return await storage.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

}

public sealed record FileUploadResponse(string Id, string OriginalFileName, string ContentType, long Length);
