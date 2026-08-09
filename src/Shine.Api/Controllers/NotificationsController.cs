using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;
using Shine.Api;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
[RequiresModule("CORE")]
public sealed class NotificationsController(
    ShineDbContext dbContext,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<NotificationResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantId = RequireTenant();
        var notifications = await dbContext.Notifications
            .AsNoTracking()
            .Where(notification => notification.TenantId == tenantId &&
                (notification.RecipientUserId == userId || notification.RecipientUserId == null))
            .OrderByDescending(notification => notification.ReadAtUtc == null)
            .ThenByDescending(notification => notification.Id)
            .Select(notification => new NotificationResponse(notification.Id, notification.TenantId, notification.RecipientUserId, notification.Type, notification.Title, notification.Message, notification.DataJson, notification.ReadAtUtc))
            .ToArrayAsync(cancellationToken);

        return Ok(notifications);
    }

    [HttpPost]
    public async Task<ActionResult<NotificationResponse>> Create(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        RequireUser();
        var tenantId = RequireTenant();
        var notification = Notification.Create(tenantId, request.RecipientUserId, request.Type, request.Title, request.Message, request.DataJson);
        dbContext.Notifications.Add(notification);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Created($"api/notifications/{notification.Id}", ToResponse(notification));
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantId = RequireTenant();
        var notification = await dbContext.Notifications.SingleOrDefaultAsync(item => item.Id == id && item.TenantId == tenantId && (item.RecipientUserId == userId || item.RecipientUserId == null), cancellationToken);
        if (notification is null) return NotFound();
        notification.MarkAsRead(DateTime.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantId = RequireTenant();
        var notifications = await dbContext.Notifications.Where(item => item.TenantId == tenantId && item.ReadAtUtc == null && (item.RecipientUserId == userId || item.RecipientUserId == null)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        notifications.ForEach(notification => notification.MarkAsRead(now));
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Guid RequireUser() => currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");
    private Guid RequireTenant() => currentTenant.TenantId ?? throw new TenantIsolationException("An active tenant is required.");
    private static NotificationResponse ToResponse(Notification notification) => new(notification.Id, notification.TenantId, notification.RecipientUserId, notification.Type, notification.Title, notification.Message, notification.DataJson, notification.ReadAtUtc);
}
