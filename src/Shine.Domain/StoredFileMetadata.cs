namespace Shine.Domain;

public enum StoredFileDeletionStatus { Active, Pending }

public sealed class StoredFileMetadata : AuditableEntity, IMultiTenantEntity
{
    private StoredFileMetadata() { }

    public StoredFileMetadata(Guid tenantId, Guid createdByUserId, string storageId, string originalFileName,
        string contentType, long length, string purpose, string readPermissionCode, string managePermissionCode)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant is required.", nameof(tenantId));
        if (createdByUserId == Guid.Empty) throw new ArgumentException("Creator is required.", nameof(createdByUserId));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Id = Guid.NewGuid(); TenantId = tenantId; OwnerUserId = createdByUserId;
        StorageId = Required(storageId, nameof(storageId), 64);
        OriginalFileName = Required(originalFileName, nameof(originalFileName), 260);
        ContentType = Required(contentType, nameof(contentType), 160);
        Length = length;
        Purpose = Required(purpose, nameof(purpose), 120).ToUpperInvariant();
        ReadPermissionCode = Required(readPermissionCode, nameof(readPermissionCode), 120);
        ManagePermissionCode = Required(managePermissionCode, nameof(managePermissionCode), 120);
    }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid OwnerUserId { get; private set; }
    public string StorageId { get; private set; } = null!;
    public string OriginalFileName { get; private set; } = null!;
    public string ContentType { get; private set; } = null!;
    public long Length { get; private set; }
    public string Purpose { get; private set; } = null!;
    public string ReadPermissionCode { get; private set; } = null!;
    public string ManagePermissionCode { get; private set; } = null!;
    public StoredFileDeletionStatus DeletionStatus { get; private set; }
    public DateTime? DeletionRequestedAtUtc { get; private set; }
    public DateTime? NextDeletionAttemptAtUtc { get; private set; }
    public int DeletionAttempts { get; private set; }
    public string? LastDeletionError { get; private set; }

    public void RequestDeletion(DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc) throw new ArgumentException("Deletion time must be UTC.", nameof(utcNow));
        if (DeletionStatus == StoredFileDeletionStatus.Pending) return;
        DeletionStatus = StoredFileDeletionStatus.Pending;
        DeletionRequestedAtUtc = utcNow;
        NextDeletionAttemptAtUtc = utcNow;
        LastDeletionError = null;
    }

    public void RecordDeletionFailure(DateTime utcNow, string error)
    {
        if (DeletionStatus != StoredFileDeletionStatus.Pending) throw new InvalidOperationException("File deletion was not requested.");
        DeletionAttempts++;
        NextDeletionAttemptAtUtc = utcNow.AddMinutes(Math.Min(30, Math.Max(1, DeletionAttempts)));
        LastDeletionError = string.IsNullOrWhiteSpace(error) ? "Storage deletion failed." : error[..Math.Min(error.Length, 2000)];
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength) throw new ArgumentException($"{name} is invalid.", name);
        return value.Trim();
    }
}
