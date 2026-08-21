namespace Shine.Api.Controllers;

public sealed record CreateNotificationRequest(Guid? RecipientUserId, string Type, string Title, string Message, string? DataJson);
public sealed record NotificationResponse(Guid Id, Guid TenantId, Guid? RecipientUserId, string Type, string Title, string Message, string? DataJson, DateTime? ReadAtUtc);
