using Shine.Domain;

namespace Shine.UnitTests;

public sealed class FeatureFlagTests
{
    [Fact]
    public void Flag_key_is_normalized_and_can_change_state()
    {
        var flag = new FeatureFlag(" new.dashboard ", false);

        flag.SetEnabled(true);

        Assert.Equal("NEW.DASHBOARD", flag.Key);
        Assert.True(flag.Enabled);
    }

    [Fact]
    public void Invalid_flag_key_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => new FeatureFlag("bad key", true));
    }
}
