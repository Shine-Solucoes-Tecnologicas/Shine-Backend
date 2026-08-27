using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Shine.Infrastructure;
using Shine.Api;
using Shine.Domain;
using Shine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
[RequiresModule("CORE")]
public sealed class FilesController(
    IFileStorage storage,
    ShineDbContext db,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IPermissionAuthorization permissions,
    StoredFileDeletionProcessor deletions) : ControllerBase
{
    [HttpPost]
    [RequiresPermission("files.manage")]
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
            var userId = RequireUser();
            var tenantId = RequireTenant();
            if (!await permissions.HasPermissionAsync(userId, tenantId, "files.manage", cancellationToken)) return Forbid();
            var stored = await storage.SaveAsync(content, file.FileName, file.ContentType, cancellationToken);
            try
            {
                var metadata = new StoredFileMetadata(tenantId, userId, stored.Id, stored.OriginalFileName,
                    stored.ContentType, stored.Length, "GENERAL", "files.read", "files.manage");
                db.StoredFiles.Add(metadata);
                await db.SaveChangesAsync(cancellationToken);
                var response = new FileUploadResponse(metadata.Id, stored.OriginalFileName, stored.ContentType, stored.Length);
                return CreatedAtAction(nameof(Download), new { id = metadata.Id }, response);
            }
            catch
            {
                await storage.DeleteAsync(stored.Id, cancellationToken);
                throw;
            }
        }
        catch (InvalidDataException exception)
        {
            return BadRequest(new { code = "invalid_file", message = exception.Message });
        }
    }

    [HttpGet("{id}")]
    [RequiresPermission("files.read")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(string id, CancellationToken cancellationToken)
    {
        try
        {
            if (!Guid.TryParse(id, out var metadataId)) return NotFound();
            var userId = RequireUser();
            var tenantId = RequireTenant();
            var metadata = await db.StoredFiles.AsNoTracking().SingleOrDefaultAsync(
                x => x.Id == metadataId && x.TenantId == tenantId &&
                    x.DeletionStatus == StoredFileDeletionStatus.Active,
                cancellationToken);
            if (metadata is null) return NotFound();
            if (!await permissions.HasPermissionAsync(userId, tenantId, metadata.ReadPermissionCode, cancellationToken)) return Forbid();
            var content = await storage.OpenReadAsync(metadata.StorageId, cancellationToken);
            return content is null ? NotFound() : File(content, metadata.ContentType, metadata.OriginalFileName, enableRangeProcessing: true);
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    [HttpDelete("{id}")]
    [RequiresPermission("files.manage")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        try
        {
            if (!Guid.TryParse(id, out var metadataId)) return NotFound();
            var userId = RequireUser();
            var tenantId = RequireTenant();
            var metadata = await db.StoredFiles.SingleOrDefaultAsync(
                x => x.Id == metadataId && x.TenantId == tenantId, cancellationToken);
            if (metadata is null) return NotFound();
            if (!await permissions.HasPermissionAsync(userId, tenantId, metadata.ManagePermissionCode, cancellationToken)) return Forbid();
            metadata.RequestDeletion(DateTime.UtcNow);
            await db.SaveChangesAsync(cancellationToken);
            var result = await deletions.ProcessAsync(tenantId, metadata.Id, cancellationToken);
            return result == StoredFileDeletionResult.Pending
                ? Accepted(new { id = metadata.Id, status = "deletion_pending" })
                : NoContent();
        }
        catch (ArgumentException)
        {
            return NotFound();
        }
    }

    private Guid RequireUser() => currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");
    private Guid RequireTenant() => currentTenant.TenantId ?? throw new TenantIsolationException("An active tenant is required.");

}

public sealed record FileUploadResponse(Guid Id, string OriginalFileName, string ContentType, long Length);
