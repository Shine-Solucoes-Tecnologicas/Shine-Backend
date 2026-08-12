using Shine.Domain;

namespace Shine.UnitTests;

public sealed class CoreContractValidationTests
{
    [Fact]
    public void EnsureUtc_accepts_only_utc_values()
    {
        var value = DateTime.SpecifyKind(new DateTime(2026, 8, 11), DateTimeKind.Utc);

        Assert.Equal(value, CoreContractValidation.EnsureUtc(value, "value"));
        Assert.Throws<ArgumentException>(() => CoreContractValidation.EnsureUtc(new DateTime(2026, 8, 11), "value"));
    }

    [Fact]
    public void EnsureTenant_and_entity_reject_empty_ids()
    {
        Assert.Throws<ArgumentException>(() => CoreContractValidation.EnsureTenant(Guid.Empty));
        Assert.Throws<ArgumentException>(() => CoreContractValidation.EnsureEntity(Guid.Empty));
    }
}
