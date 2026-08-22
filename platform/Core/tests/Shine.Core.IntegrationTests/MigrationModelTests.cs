using Microsoft.EntityFrameworkCore;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class MigrationModelTests(DatabaseFixture fixture)
{
    [Fact]
    public void Core_billing_and_scheduling_models_have_no_pending_migration_changes()
    {
        using var core = fixture.CreateDb();
        using var billing = fixture.CreateBillingDb();
        using var scheduling = fixture.CreateSchedulingDb();

        Assert.False(core.Database.HasPendingModelChanges());
        Assert.False(billing.Database.HasPendingModelChanges());
        Assert.False(scheduling.Database.HasPendingModelChanges());
    }
}
