using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Api.Controllers;
using Shine.Application;
using Shine.Domain;
using Shine.Domain.Identity;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class AdministrativeApiTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Administrative_dashboard_reports_status_period_and_minimized_recent_events()
    {
        var now = DateTime.UtcNow;
        var actorId = Guid.NewGuid();
        var activeTenant = new Tenant($"Dashboard active {Guid.NewGuid():N}");
        var suspendedTenant = new Tenant($"Dashboard suspended {Guid.NewGuid():N}");
        suspendedTenant.Suspend("Dashboard test", actorId, now);
        var activeUser = new User($"dashboard-active-{Guid.NewGuid():N}@example.test", "hash");
        var blockedUser = new User($"dashboard-blocked-{Guid.NewGuid():N}@example.test", "hash");
        blockedUser.Block("Dashboard test", actorId, now);
        var recentEvent = AuditEntry.Create(
            "AdministrativeDashboardTest",
            Guid.NewGuid().ToString(),
            $"DASH_{Guid.NewGuid():N}"[..25],
            actorId,
            null,
            now.AddMinutes(30),
            newValuesJson: "{\"sensitive\":\"must-not-be-projected\"}");

        await using var db = fixture.CreateDb();
        db.AddRange(activeTenant, suspendedTenant, activeUser, blockedUser, recentEvent,
            AuditEntry.Create("AdministrativeDashboardTest", Guid.NewGuid().ToString(), "OLD", actorId, null, now.AddDays(-2)),
            AuditEntry.Create("TenantEvent", Guid.NewGuid().ToString(), "TENANT", actorId, activeTenant.Id, now),
            OperationalLog.Create("Information", "Dashboard", "Recent", createdAtUtc: now),
            OperationalLog.Create("Information", "Dashboard", "Old", createdAtUtc: now.AddDays(-2)));
        await db.SaveChangesAsync();

        var fromUtc = now.AddHours(-1);
        var toUtc = now.AddHours(1);
        var action = await new AdministrativeDashboardController(db)
            .Summary(new AdministrativeDashboardRequest(fromUtc, toUtc), default);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        var summary = Assert.IsType<AdministrativeDashboardSummary>(response.Value);
        Assert.Equal(fromUtc, summary.Period.FromUtc);
        Assert.Equal(toUtc, summary.Period.ToUtc);
        Assert.Equal(await db.Tenants.CountAsync(), summary.Tenants.Total);
        Assert.Equal(await db.Tenants.CountAsync(x => x.IsActive), summary.Tenants.Active);
        Assert.Equal(await db.Tenants.CountAsync(x => !x.IsActive), summary.Tenants.Suspended);
        Assert.Equal(await db.Users.CountAsync(), summary.Users.Total);
        Assert.Equal(await db.Users.CountAsync(x => x.IsActive), summary.Users.Active);
        Assert.Equal(await db.Users.CountAsync(x => !x.IsActive), summary.Users.Blocked);
        Assert.Equal(await db.AuditEntries.CountAsync(x =>
            x.TenantId == null && x.OccurredAtUtc >= fromUtc && x.OccurredAtUtc < toUtc), summary.AuditEvents);
        Assert.Equal(await db.OperationalLogs.CountAsync(x =>
            x.CreatedAtUtc >= fromUtc && x.CreatedAtUtc < toUtc), summary.OperationalLogs);
        Assert.Contains(summary.RecentEvents, x => x.Id == recentEvent.Id);
        Assert.All(summary.RecentEvents, x => Assert.Null(x.GetType().GetProperty("NewValuesJson")));
    }

    [Fact]
    public async Task Administrative_dashboard_rejects_invalid_or_excessive_periods()
    {
        await using var db = fixture.CreateDb();
        var controller = new AdministrativeDashboardController(db);
        var now = DateTime.UtcNow;

        var inverted = await controller.Summary(new AdministrativeDashboardRequest(now, now), default);
        var invertedError = Assert.IsType<BadRequestObjectResult>(inverted.Result);
        Assert.Equal("dashboard_period_invalid",
            Assert.IsType<AdministrativeDashboardError>(invertedError.Value).Code);

        var excessive = await controller.Summary(
            new AdministrativeDashboardRequest(now.AddDays(-91), now), default);
        var excessiveError = Assert.IsType<BadRequestObjectResult>(excessive.Result);
        Assert.Equal("dashboard_period_too_large",
            Assert.IsType<AdministrativeDashboardError>(excessiveError.Value).Code);
    }

    [Fact]
    public async Task Administrative_tenant_list_and_detail_return_existing_tenant()
    {
        await using var db = fixture.CreateDb();
        var tenant = new Tenant($"Administrative API {Guid.NewGuid():N}");
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();

        var controller = new AdministrativeTenantsController(db, new FakeCurrentUser());
        var list = await controller.List(new PagedRequest(PageSize: 100), tenant.Name, null, CancellationToken.None);
        var listResponse = Assert.IsType<OkObjectResult>(list.Result);
        var page = Assert.IsType<PagedResponse<AdministrativeTenantResponse>>(listResponse.Value);
        Assert.Contains(page.Items, item => item.Id == tenant.Id);

        var detail = await controller.Detail(tenant.Id, CancellationToken.None);
        var detailResponse = Assert.IsType<OkObjectResult>(detail.Result);
        var item = Assert.IsType<AdministrativeTenantResponse>(detailResponse.Value);
        Assert.Equal(tenant.Id, item.Id);
        Assert.Equal(tenant.Name, item.Name);
    }

    [Fact]
    public async Task Suspending_one_tenant_revokes_only_sessions_bound_to_that_tenant()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"multi-session-{Guid.NewGuid():N}@example.test", "hash");
        var suspendedTenant = new Tenant($"Suspended {Guid.NewGuid():N}");
        var activeTenant = new Tenant($"Active {Guid.NewGuid():N}");
        var suspendedMembership = new UserTenant(user.Id, suspendedTenant.Id, user.Id, false);
        var activeMembership = new UserTenant(user.Id, activeTenant.Id, user.Id, false);
        var suspendedSession = new RefreshToken(user.Id, suspendedTenant.Id, suspendedMembership.UserTenantId,
            $"suspended-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(1));
        var activeSession = new RefreshToken(user.Id, activeTenant.Id, activeMembership.UserTenantId,
            $"active-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(1));
        var globalSession = new RefreshToken(user.Id, null, null,
            $"global-{Guid.NewGuid():N}", DateTime.UtcNow.AddDays(1));
        db.AddRange(user, suspendedTenant, activeTenant, suspendedMembership, activeMembership,
            suspendedSession, activeSession, globalSession);
        await db.SaveChangesAsync();

        var response = await new AdministrativeTenantsController(db, new FakeCurrentUser())
            .Suspend(suspendedTenant.Id, new SuspendTenantRequest("Security test"), default);

        Assert.IsType<NoContentResult>(response);
        Assert.NotNull(suspendedSession.RevokedAtUtc);
        Assert.Null(activeSession.RevokedAtUtc);
        Assert.Null(globalSession.RevokedAtUtc);
        Assert.False(suspendedTenant.IsActive);
        Assert.True(activeTenant.IsActive);
    }

    private sealed class FakeCurrentUser : Shine.Infrastructure.ICurrentUser
    {
        public Guid? UserId => Guid.NewGuid();
        public bool IsAuthenticated => true;
    }
}
