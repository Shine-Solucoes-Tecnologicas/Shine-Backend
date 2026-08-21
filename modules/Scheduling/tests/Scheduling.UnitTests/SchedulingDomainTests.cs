using Scheduling.Domain;

namespace Shine.UnitTests;

public sealed class SchedulingDomainTests
{
    [Fact]
    public void Service_requires_positive_duration()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Service(Guid.NewGuid(), "Corte", 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Service(Guid.NewGuid(), "Corte", 1441));
    }

    [Fact]
    public void Professional_service_accepts_optional_override_and_can_be_disabled()
    {
        var link = new ProfessionalService(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 45);
        link.SetActive(false);
        Assert.Equal(45, link.DurationOverrideMinutes);
        Assert.False(link.IsActive);
    }

    [Fact]
    public void Professional_service_rejects_invalid_override()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProfessionalService(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0));
    }
}
