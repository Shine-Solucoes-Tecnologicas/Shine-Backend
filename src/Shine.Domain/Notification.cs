namespace Shine.Domain;

public sealed class Notification : AuditableEntity, IMultiTenantEntity
{
    private Notification() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? RecipientUserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Message { get; private set; } = string.Empty;
    public string? DataJson { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }

    public static Notification Create(Guid tenantId, Guid? recipientUserId, string type, string title, string message, string? dataJson = null) => new()
    {
        Id = Guid.NewGuid(), TenantId = tenantId, RecipientUserId = recipientUserId,
        Type = Required(type, nameof(type)), Title = Required(title, nameof(title)),
        Message = Required(message, nameof(message)), DataJson = dataJson
    };

    public void MarkAsRead(DateTime nowUtc) => ReadAtUtc = nowUtc.Kind == DateTimeKind.Utc ? nowUtc : nowUtc.ToUniversalTime();

    private static string Required(string value, string name) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Value is required.", name) : value.Trim();
}
