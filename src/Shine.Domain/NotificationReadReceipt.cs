namespace Shine.Domain;

public sealed class NotificationReadReceipt : IMultiTenantEntity
{
    private NotificationReadReceipt() { }

    public NotificationReadReceipt(Guid tenantId, Guid notificationId, Guid userId, DateTime readAtUtc)
    {
        if (tenantId == Guid.Empty || notificationId == Guid.Empty || userId == Guid.Empty)
            throw new ArgumentException("Tenant, notification and user are required.");
        TenantId = tenantId; NotificationId = notificationId; UserId = userId;
        MarkAsRead(readAtUtc);
    }

    public Guid TenantId { get; private set; }
    public Guid NotificationId { get; private set; }
    public Guid UserId { get; private set; }
    public DateTime ReadAtUtc { get; private set; }

    public void MarkAsRead(DateTime nowUtc) => ReadAtUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();
}
