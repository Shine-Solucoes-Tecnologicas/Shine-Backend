using Shine.Domain;

namespace Shine.UnitTests;

public sealed class CustomerTests
{
    [Fact]
    public void Customer_normalizes_contacts_and_preserves_tenant_identity()
    {
        var tenantId = Guid.NewGuid();

        var customer = new Customer(
            tenantId,
            "  Maria da Silva  ",
            "  MARIA@Example.com ",
            "+55 (11) 99999-1234",
            "123.456.789-01");

        Assert.NotEqual(Guid.Empty, customer.Id);
        Assert.Equal(tenantId, customer.TenantId);
        Assert.Equal("Maria da Silva", customer.Name);
        Assert.Equal("MARIA DA SILVA", customer.NormalizedName);
        Assert.Equal("maria@example.com", customer.Email);
        Assert.Equal("5511999991234", customer.Phone);
        Assert.Equal("12345678901", customer.TaxIdentifier);
        Assert.True(customer.IsActive);
    }

    [Fact]
    public void Customer_can_update_deactivate_and_reactivate_without_changing_identity()
    {
        var tenantId = Guid.NewGuid();
        var customer = new Customer(tenantId, "Initial");
        var customerId = customer.Id;

        customer.Update("Updated", "updated@example.com", "11988887777", "12.345.678/0001-90");
        customer.Deactivate(DateTime.UtcNow);

        Assert.Equal(customerId, customer.Id);
        Assert.Equal(tenantId, customer.TenantId);
        Assert.Equal("Updated", customer.Name);
        Assert.Equal("12345678000190", customer.TaxIdentifier);
        Assert.False(customer.IsActive);

        customer.Reactivate();

        Assert.True(customer.IsActive);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Customer_requires_name(string name)
    {
        Assert.Throws<ArgumentException>(() => new Customer(Guid.NewGuid(), name));
    }

    [Fact]
    public void Customer_rejects_invalid_tenant_email_and_tax_identifier()
    {
        Assert.Throws<ArgumentException>(() => new Customer(Guid.Empty, "Customer"));
        Assert.Throws<ArgumentException>(() => new Customer(Guid.NewGuid(), "Customer", "not-an-email"));
        Assert.Throws<ArgumentException>(() => new Customer(Guid.NewGuid(), "Customer", taxIdentifier: "123"));
    }
}
