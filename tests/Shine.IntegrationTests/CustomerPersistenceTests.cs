using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class CustomerPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Customers_are_filtered_by_tenant_and_soft_delete()
    {
        var tenantA = new Tenant($"Customer unit A {Guid.NewGuid():N}");
        var tenantB = new Tenant($"Customer unit B {Guid.NewGuid():N}");
        var activeA = new Customer(tenantA.Id, "Active A", taxIdentifier: "123.456.789-01");
        var deletedA = new Customer(tenantA.Id, "Deleted A");
        var activeB = new Customer(tenantB.Id, "Active B", taxIdentifier: "123.456.789-01");
        deletedA.Deactivate(DateTime.UtcNow);

        await using (var seedDb = fixture.CreateDb())
        {
            seedDb.Tenants.AddRange(tenantA, tenantB);
            seedDb.Customers.AddRange(activeA, deletedA, activeB);
            await seedDb.SaveChangesAsync();
        }

        await using var tenantDb = fixture.CreateDb(new FakeTenant(tenantA.Id));
        var visible = await tenantDb.Customers
            .Where(x => x.Id == activeA.Id || x.Id == deletedA.Id || x.Id == activeB.Id)
            .ToArrayAsync();

        Assert.Equal(activeA.Id, Assert.Single(visible).Id);
    }

    [Fact]
    public async Task Tax_identifier_is_unique_only_among_active_customers_in_same_tenant()
    {
        var tenant = new Tenant($"Customer uniqueness {Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDb())
        {
            seedDb.Tenants.Add(tenant);
            seedDb.Customers.Add(new Customer(tenant.Id, "First", taxIdentifier: "987.654.321-00"));
            await seedDb.SaveChangesAsync();
        }

        await using (var duplicateDb = fixture.CreateDb(new FakeTenant(tenant.Id)))
        {
            duplicateDb.Customers.Add(new Customer(tenant.Id, "Duplicate", taxIdentifier: "98765432100"));
            await Assert.ThrowsAsync<DbUpdateException>(() => duplicateDb.SaveChangesAsync());
        }

        await using (var deactivateDb = fixture.CreateDb(new FakeTenant(tenant.Id)))
        {
            var existing = await deactivateDb.Customers.SingleAsync(x => x.TaxIdentifier == "98765432100");
            existing.Deactivate(DateTime.UtcNow);
            await deactivateDb.SaveChangesAsync();
        }

        await using var replacementDb = fixture.CreateDb(new FakeTenant(tenant.Id));
        replacementDb.Customers.Add(new Customer(tenant.Id, "Replacement", taxIdentifier: "98765432100"));
        await replacementDb.SaveChangesAsync();
    }

    [Fact]
    public async Task Cross_tenant_write_is_rejected_and_customer_pii_is_masked_in_audit()
    {
        var tenant = new Tenant($"Customer audit {Guid.NewGuid():N}");
        await using (var seedDb = fixture.CreateDb())
        {
            seedDb.Tenants.Add(tenant);
            await seedDb.SaveChangesAsync();
        }

        var otherTenantId = Guid.NewGuid();
        await using (var scopedDb = fixture.CreateDb(new FakeTenant(tenant.Id)))
        {
            scopedDb.Customers.Add(new Customer(otherTenantId, "Denied"));
            await Assert.ThrowsAsync<TenantIsolationException>(() => scopedDb.SaveChangesAsync());
        }

        var customer = new Customer(tenant.Id, "Audited", "audit@example.com", "+55 11 99999-0000", "111.222.333-44");
        await using (var scopedDb = fixture.CreateDb(new FakeTenant(tenant.Id)))
        {
            scopedDb.Customers.Add(customer);
            await scopedDb.SaveChangesAsync();
        }

        await using var verificationDb = fixture.CreateDb();
        var audit = await verificationDb.AuditEntries
            .Where(x => x.EntityType == nameof(Customer) && x.EntityId == customer.Id.ToString())
            .OrderByDescending(x => x.OccurredAtUtc)
            .FirstAsync();

        Assert.DoesNotContain("audit@example.com", audit.NewValuesJson);
        Assert.DoesNotContain("5511999990000", audit.NewValuesJson);
        Assert.DoesNotContain("11122233344", audit.NewValuesJson);
        Assert.Contains("[MASKED]", audit.NewValuesJson);
    }

    private sealed class FakeTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }
}
