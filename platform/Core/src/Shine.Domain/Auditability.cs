namespace Shine.Domain;

public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTime? DeletedAtUtc { get; }
    void Delete(DateTime nowUtc);
    void Restore();
}

public interface IAuditableEntity
{
    Guid? CreatedByUserId { get; }
    Guid? CreatedTenantId { get; }
    DateTime CreatedAtUtc { get; }
    Guid? UpdatedByUserId { get; }
    DateTime? UpdatedAtUtc { get; }
}

public abstract class AuditableEntity : IAuditableEntity, ISoftDeletable
{
    public Guid? CreatedByUserId { get; private set; }
    public Guid? CreatedTenantId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid? UpdatedByUserId { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    public void MarkCreated(Guid? userId, Guid? tenantId, DateTime nowUtc)
    {
        CreatedByUserId = userId;
        CreatedTenantId = tenantId;
        CreatedAtUtc = EnsureUtc(nowUtc);
        UpdatedByUserId = null;
        UpdatedAtUtc = null;
    }

    public void MarkUpdated(Guid? userId, DateTime nowUtc)
    {
        if (CreatedAtUtc == default) throw new DomainException("An entity must be marked as created before it can be updated.");
        UpdatedByUserId = userId;
        UpdatedAtUtc = EnsureUtc(nowUtc);
    }

    public void Delete(DateTime nowUtc)
    {
        IsDeleted = true;
        DeletedAtUtc = EnsureUtc(nowUtc);
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAtUtc = null;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value.ToUniversalTime()
    };
}
