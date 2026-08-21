using Shine.Domain;

namespace Shine.UnitTests;

public sealed class SchedulingPoliciesTests
{
    [Fact]
    public void WarnAndConfirm_allows_existing_overlap_after_explicit_confirmation()
    {
        var policy = new CapacityPolicy(2, ConflictMode.WarnAndConfirm);

        Assert.False(policy.Blocks(1));
        Assert.True(policy.RequiresConfirmation(1));
        Assert.True(policy.Blocks(2));
    }

    [Fact]
    public void Allow_does_not_require_confirmation_until_capacity_is_reached()
    {
        var policy = new CapacityPolicy(3, ConflictMode.Allow);

        Assert.False(policy.RequiresConfirmation(2));
        Assert.False(policy.Blocks(2));
        Assert.True(policy.Blocks(3));
    }

    [Fact]
    public void Block_rejects_any_overlap()
    {
        var policy = new CapacityPolicy(5, ConflictMode.Block);

        Assert.True(policy.Blocks(1));
    }
}
