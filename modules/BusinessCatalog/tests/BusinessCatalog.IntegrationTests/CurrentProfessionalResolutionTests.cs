using BusinessCatalog.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class CurrentProfessionalResolutionTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Reader_resolves_user_only_inside_requested_unit_and_preserves_inactive_state()
    {
        var unitA = Guid.NewGuid();
        var unitB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var active = new Professional(unitA, "Active", userId);
        var foreign = new Professional(unitB, "Foreign", userId);
        var inactiveUserId = Guid.NewGuid();
        var inactive = new Professional(unitA, "Inactive", inactiveUserId);
        inactive.SetActive(false);

        await using var db = fixture.CreateBusinessCatalogDb();
        db.Professionals.AddRange(active, foreign, inactive);
        await db.SaveChangesAsync();
        var reader = new BusinessCatalogReader(db);

        var resolved = await reader.FindProfessionalByUserAsync(unitA, userId);
        var crossTenant = await reader.FindProfessionalByUserAsync(Guid.NewGuid(), userId);
        var missing = await reader.FindProfessionalByUserAsync(unitA, Guid.NewGuid());
        var inactiveResolved = await reader.FindProfessionalByUserAsync(unitA, inactiveUserId);

        Assert.Equal(active.Id, resolved?.Id);
        Assert.Null(crossTenant);
        Assert.Null(missing);
        Assert.False(inactiveResolved?.IsActive);
    }

    [Fact]
    public async Task Database_rejects_two_professionals_linked_to_same_user_in_one_unit()
    {
        var unitId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await using var db = fixture.CreateBusinessCatalogDb();
        db.Professionals.AddRange(
            new Professional(unitId, "First", userId),
            new Professional(unitId, "Second", userId));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
