using Microsoft.EntityFrameworkCore;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class BackendFlowIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Registration_persists_tenant_membership_roles_and_audit_entries()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"integration-{Guid.NewGuid():N}@example.test", "hash");
        var tenant = new Tenant($"Integration {Guid.NewGuid():N}");
        db.Users.Add(user);
        db.Tenants.Add(tenant);
        db.UserTenants.Add(new UserTenant(user.Id, tenant.Id, user.Id, true));
        await db.SaveChangesAsync();
        await AuthorizationSeed.SeedTenantDefaultsAsync(db, tenant.Id, user.Id, user.Id);

        Assert.True(await db.Users.AnyAsync(item => item.Id == user.Id));
        Assert.True(await db.UserTenants.AnyAsync(item => item.UserId == user.Id && item.TenantId == tenant.Id));
        Assert.Contains(Role.OwnerName, await db.UserTenantRoles.Where(item => item.UserId == user.Id && item.TenantId == tenant.Id).Select(item => item.Role.Name).ToArrayAsync());
        Assert.True(await db.AuditEntries.AnyAsync(item => item.EntityType == nameof(User) && item.EntityId == user.Id.ToString()));
    }

    [Fact]
    public async Task Revoked_refresh_session_is_not_active_after_persistence()
    {
        await using var db = fixture.CreateDb();
        var user = new User($"session-{Guid.NewGuid():N}@example.test", "hash");
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var token = new RefreshToken(user.Id, null, null, $"hash-{Guid.NewGuid():N}", DateTime.UtcNow.AddHours(1));
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        token.Revoke(DateTime.UtcNow);
        await db.SaveChangesAsync();

        var stored = await db.RefreshTokens.SingleAsync(item => item.Id == token.Id);
        Assert.False(stored.IsActive(DateTime.UtcNow));
        Assert.NotNull(stored.RevokedAtUtc);
    }
}
