using Microsoft.AspNetCore.Authorization;
using Shine.Api;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class PermissionHandlerTests
{
    [Fact]
    public async Task Handler_succeeds_for_permission_in_active_tenant()
    {
        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var handler = new PermissionHandler(new FakeUser(userId), new FakeTenant(tenantId), new FakePermissions(true));
        var requirement = new PermissionRequirement("users.read");
        var context = new AuthorizationHandlerContext([requirement], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_denies_permission_without_complete_context()
    {
        var handler = new PermissionHandler(new FakeUser(Guid.NewGuid()), new FakeTenant(null), new FakePermissions(true));
        var requirement = new PermissionRequirement("users.read");
        var context = new AuthorizationHandlerContext([requirement], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task Global_handler_succeeds_for_global_permission_without_tenant_context()
    {
        var handler = new GlobalPermissionHandler(new FakeUser(Guid.NewGuid()), new FakePermissions(true, true));
        var requirement = new GlobalPermissionRequirement("admin.read");
        var context = new AuthorizationHandlerContext([requirement], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Global_handler_denies_when_global_permission_is_missing()
    {
        var handler = new GlobalPermissionHandler(new FakeUser(Guid.NewGuid()), new FakePermissions(true, false));
        var requirement = new GlobalPermissionRequirement("admin.read");
        var context = new AuthorizationHandlerContext([requirement], new System.Security.Claims.ClaimsPrincipal(), null);

        await handler.HandleAsync(context);

        Assert.False(context.HasSucceeded);
    }

    private sealed class FakeUser(Guid id) : ICurrentUser
    {
        public Guid? UserId => id;
        public bool IsAuthenticated => true;
    }

    private sealed class FakeTenant(Guid? id) : ICurrentTenant
    {
        public Guid? TenantId => id;
        public Guid? UserTenantId => id;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => id is not null;
    }

    private sealed class FakePermissions(bool allowed, bool globalAllowed = false) : IPermissionAuthorization
    {
        public Task<bool> HasPermissionAsync(Guid userId, Guid tenantId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(allowed);
        public Task<bool> HasGlobalPermissionAsync(Guid userId, string permissionCode, CancellationToken cancellationToken = default) => Task.FromResult(globalAllowed);
    }
}
