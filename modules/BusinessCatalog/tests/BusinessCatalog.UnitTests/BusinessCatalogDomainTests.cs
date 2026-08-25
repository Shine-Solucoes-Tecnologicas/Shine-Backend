using BusinessCatalog.Domain;

namespace BusinessCatalog.UnitTests;

public sealed class BusinessCatalogDomainTests
{
    [Fact]
    public void Professional_requires_a_unit_and_a_name()
    {
        Assert.Throws<ArgumentException>(() => new Professional(Guid.Empty, "Ana"));
        Assert.Throws<ArgumentException>(() => new Professional(Guid.NewGuid(), " "));
    }

    [Fact]
    public void Service_validates_its_base_duration()
    {
        var service = new Service(Guid.NewGuid(), "Corte", 30);
        Assert.Equal(30, service.DurationMinutes);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.SetDuration(0));
    }

    [Fact]
    public void Association_is_reactivated_without_replacing_its_identity()
    {
        var association = new ProfessionalService(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        association.SetActive(false);
        association.SetActive(true);

        Assert.True(association.IsActive);
    }
}
