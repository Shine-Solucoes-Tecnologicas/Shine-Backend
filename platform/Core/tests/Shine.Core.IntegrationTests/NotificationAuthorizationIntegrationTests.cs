using Microsoft.AspNetCore.Mvc;
using Shine.Api.Controllers;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class NotificationAuthorizationIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Creation_requires_permission_and_rejects_recipient_from_another_tenant()
    {
        var setup = await CreateUsersAsync();
        var request = new CreateNotificationRequest(setup.UserB.Id, "test", "Title", "Message", null);

        await using (var db = fixture.CreateDb(new TestTenant(setup.TenantA.Id)))
        {
            var denied = new NotificationsController(db, new TestUser(setup.UserA.Id), new TestTenant(setup.TenantA.Id), new TestPermissions(false));
            Assert.IsType<ForbidResult>((await denied.Create(request, default)).Result);
        }

        await using (var db = fixture.CreateDb(new TestTenant(setup.TenantA.Id)))
        {
            var allowed = new NotificationsController(db, new TestUser(setup.UserA.Id), new TestTenant(setup.TenantA.Id), new TestPermissions(true));
            Assert.IsType<BadRequestObjectResult>((await allowed.Create(request, default)).Result);
        }
    }

    [Fact]
    public async Task Collective_notification_read_state_is_individual_per_user()
    {
        var setup = await CreateUsersAsync(includeSecondUserInTenantA: true);
        var notification = Notification.Create(setup.TenantA.Id, null, "collective", "Title", "Message");
        await using (var seed = fixture.CreateDb())
        {
            seed.Notifications.Add(notification);
            await seed.SaveChangesAsync();
        }

        await using (var db = fixture.CreateDb(new TestTenant(setup.TenantA.Id)))
        {
            var readerA = new NotificationsController(db, new TestUser(setup.UserA.Id), new TestTenant(setup.TenantA.Id), new TestPermissions(false));
            Assert.IsType<NoContentResult>(await readerA.MarkAsRead(notification.Id, default));
            var items = Assert.IsAssignableFrom<IReadOnlyCollection<NotificationResponse>>(Assert.IsType<OkObjectResult>((await readerA.List(default)).Result).Value);
            Assert.NotNull(Assert.Single(items, x => x.Id == notification.Id).ReadAtUtc);
        }

        await using (var db = fixture.CreateDb(new TestTenant(setup.TenantA.Id)))
        {
            var readerC = new NotificationsController(db, new TestUser(setup.UserC!.Id), new TestTenant(setup.TenantA.Id), new TestPermissions(false));
            var items = Assert.IsAssignableFrom<IReadOnlyCollection<NotificationResponse>>(Assert.IsType<OkObjectResult>((await readerC.List(default)).Result).Value);
            Assert.Null(Assert.Single(items, x => x.Id == notification.Id).ReadAtUtc);
        }
    }

    private async Task<Setup> CreateUsersAsync(bool includeSecondUserInTenantA = false)
    {
        var tenantA = new Tenant($"Notification A {Guid.NewGuid():N}");
        var tenantB = new Tenant($"Notification B {Guid.NewGuid():N}");
        var userA = new User($"notification-a-{Guid.NewGuid():N}@example.test", "hash");
        var userB = new User($"notification-b-{Guid.NewGuid():N}@example.test", "hash");
        var userC = includeSecondUserInTenantA ? new User($"notification-c-{Guid.NewGuid():N}@example.test", "hash") : null;
        await using var db = fixture.CreateDb();
        db.AddRange(tenantA, tenantB, userA, userB);
        if (userC is not null) db.Add(userC);
        db.UserTenants.AddRange(new UserTenant(userA.Id, tenantA.Id, userA.Id, false), new UserTenant(userB.Id, tenantB.Id, userB.Id, false));
        if (userC is not null) db.UserTenants.Add(new UserTenant(userC.Id, tenantA.Id, userC.Id, false));
        await db.SaveChangesAsync();
        return new(tenantA, tenantB, userA, userB, userC);
    }

    private sealed record Setup(Tenant TenantA, Tenant TenantB, User UserA, User UserB, User? UserC);
    private sealed class TestUser(Guid id) : ICurrentUser { public Guid? UserId => id; public bool IsAuthenticated => true; }
    private sealed class TestTenant(Guid id) : ICurrentTenant { public Guid? TenantId => id; public Guid? UserTenantId => Guid.NewGuid(); public IReadOnlyCollection<string> Roles => []; public bool HasCompleteContext => true; }
    private sealed class TestPermissions(bool allowed) : IPermissionAuthorization { public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(allowed); }
}
