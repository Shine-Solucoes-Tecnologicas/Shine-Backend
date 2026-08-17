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
    ICurrentTenant currentTenant,
    IPermissionAuthorization permissions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<NotificationResponse>>> List(CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantId = RequireTenant();
        var visible =
            from notification in dbContext.Notifications.AsNoTracking()
            join receipt in dbContext.NotificationReadReceipts.AsNoTracking().Where(x => x.UserId == userId)
                on notification.Id equals receipt.NotificationId into receipts
            from receipt in receipts.DefaultIfEmpty()
            where notification.TenantId == tenantId &&
                (notification.RecipientUserId == userId || notification.RecipientUserId == null)
            select new
            {
                Notification = notification,
                ReadAtUtc = notification.RecipientUserId == null ? (DateTime?)receipt.ReadAtUtc : notification.ReadAtUtc
            };

        var notifications = await visible
            .OrderByDescending(item => item.ReadAtUtc == null)
            .ThenByDescending(item => item.Notification.Id)
            .Select(item => new NotificationResponse(item.Notification.Id, item.Notification.TenantId, item.Notification.RecipientUserId,
                item.Notification.Type, item.Notification.Title, item.Notification.Message, item.Notification.DataJson, item.ReadAtUtc))
            .ToArrayAsync(cancellationToken);

        return Ok(notifications);
    }

    [HttpPost]
    [RequiresPermission("notifications.manage")]
    public async Task<ActionResult<NotificationResponse>> Create(CreateNotificationRequest request, CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantId = RequireTenant();
        if (!await permissions.HasPermissionAsync(userId, tenantId, "notifications.manage", cancellationToken)) return Forbid();
        if (request.RecipientUserId is Guid recipientId && !await dbContext.UserTenants.AnyAsync(
                membership => membership.TenantId == tenantId && membership.UserId == recipientId && membership.IsActive, cancellationToken))
            return BadRequest(new { code = "recipient_not_in_tenant", message = "The recipient does not belong to the active organization." });
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
        var now = DateTime.UtcNow;
        if (notification.RecipientUserId is null)
        {
            var receipt = await dbContext.NotificationReadReceipts.SingleOrDefaultAsync(x => x.NotificationId == id && x.UserId == userId, cancellationToken);
            if (receipt is null) dbContext.NotificationReadReceipts.Add(new NotificationReadReceipt(tenantId, id, userId, now));
            else receipt.MarkAsRead(now);
        }
        else notification.MarkAsRead(now);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var userId = RequireUser();
        var tenantId = RequireTenant();
        var notifications = await dbContext.Notifications.Where(item => item.TenantId == tenantId && (item.RecipientUserId == userId || item.RecipientUserId == null)).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;
        foreach (var notification in notifications)
        {
            if (notification.RecipientUserId is not null)
            {
                if (notification.ReadAtUtc is null) notification.MarkAsRead(now);
                continue;
            }

            var receipt = await dbContext.NotificationReadReceipts.SingleOrDefaultAsync(x => x.NotificationId == notification.Id && x.UserId == userId, cancellationToken);
            if (receipt is null) dbContext.NotificationReadReceipts.Add(new NotificationReadReceipt(tenantId, notification.Id, userId, now));
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private Guid RequireUser() => currentUser.UserId ?? throw new UnauthorizedAccessException("An authenticated user is required.");
    private Guid RequireTenant() => currentTenant.TenantId ?? throw new TenantIsolationException("An active tenant is required.");
    private static NotificationResponse ToResponse(Notification notification) => new(notification.Id, notification.TenantId, notification.RecipientUserId, notification.Type, notification.Title, notification.Message, notification.DataJson, notification.ReadAtUtc);
}
