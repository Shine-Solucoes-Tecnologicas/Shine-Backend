using Billing.Domain;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Api.Controllers;
using Shine.Domain.Identity;
using Shine.Infrastructure;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class CommercialContractPersistenceTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task Contract_revisions_and_actor_are_persisted_as_history()
    {
        await using var db = fixture.CreateBillingDb();
        var actor = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var contract = new CommercialContract(Guid.NewGuid(), null, $"REF-{Guid.NewGuid():N}", actor,
            "Aprovação inicial", now, null, [Term("10", now)], now);
        db.CommercialContracts.Add(contract);
        await db.SaveChangesAsync();

        contract.Amend(actor, "Desconto renegociado", now.AddDays(1), null, [Term("15", now.AddDays(1))], now.AddMinutes(1));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var persisted = await db.CommercialContracts.Include(x => x.Revisions).ThenInclude(x => x.Terms)
            .SingleAsync(x => x.Id == contract.Id);
        Assert.Equal(2, persisted.Revisions.Count);
        Assert.Equal(["10", "15"], persisted.Revisions.OrderBy(x => x.RevisionNumber).SelectMany(x => x.Terms).Select(x => x.Value));
        Assert.All(persisted.Revisions, x => Assert.Equal(actor, x.ActorUserId));
    }

    [Fact]
    public async Task Customer_endpoint_returns_only_its_organization_and_hides_internal_terms()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var firstAccount = new CustomerAccount($"Commercial A {Guid.NewGuid():N}");
        var secondAccount = new CustomerAccount($"Commercial B {Guid.NewGuid():N}");
        var firstUnit = new Tenant($"Commercial unit A {Guid.NewGuid():N}");
        var secondUnit = new Tenant($"Commercial unit B {Guid.NewGuid():N}");
        firstUnit.AssignToCustomerAccount(firstAccount.Id);
        secondUnit.AssignToCustomerAccount(secondAccount.Id);
        coreDb.AddRange(firstAccount, secondAccount, firstUnit, secondUnit);
        await coreDb.SaveChangesAsync();
        var now = DateTime.UtcNow;
        billingDb.CommercialContracts.AddRange(
            Contract(firstAccount.Id, now, visible: true),
            Contract(firstAccount.Id, now, visible: false),
            Contract(secondAccount.Id, now, visible: true));
        await billingDb.SaveChangesAsync();

        var controller = new CustomerCommercialContractsController(billingDb, coreDb, new TestTenant(firstUnit.Id));
        var response = await controller.List(default);
        var items = Assert.IsAssignableFrom<IReadOnlyCollection<CommercialContractResponse>>(Assert.IsType<OkObjectResult>(response.Result).Value);

        Assert.Equal(2, items.Count);
        Assert.All(items, item => Assert.Equal(firstAccount.Id, item.AccountId));
        Assert.Single(items.SelectMany(x => x.CurrentRevision.Terms));
        Assert.All(items.SelectMany(x => x.CurrentRevision.Terms), x => Assert.True(x.VisibleToCustomer));
        Assert.All(items, x => { Assert.Null(x.CurrentRevision.ActorUserId); Assert.Null(x.CurrentRevision.Justification); });
    }

    [Fact]
    public async Task Administrative_creation_validates_subscription_existence_and_organization()
    {
        await using var coreDb = fixture.CreateDb();
        await using var billingDb = fixture.CreateBillingDb();
        var account = new CustomerAccount($"Contract subscription A {Guid.NewGuid():N}");
        var otherAccount = new CustomerAccount($"Contract subscription B {Guid.NewGuid():N}");
        coreDb.AddRange(account, otherAccount);
        await coreDb.SaveChangesAsync();
        var subscription = new Subscription(otherAccount.Id, Guid.NewGuid(), new BillingInterval(BillingIntervalUnit.Month, 1));
        billingDb.Subscriptions.Add(subscription);
        await billingDb.SaveChangesAsync();
        var controller = new AdministrativeCommercialContractsController(billingDb, coreDb, new TestUser(Guid.NewGuid()));

        var unknown = await controller.Create(Request(account.Id, Guid.NewGuid()), default);
        Assert.Equal("SUBSCRIPTION_NOT_FOUND", Assert.IsType<CommercialContractError>(Assert.IsType<ObjectResult>(unknown.Result).Value).Code);

        var crossOrganization = await controller.Create(Request(account.Id, subscription.Id), default);
        Assert.Equal("SUBSCRIPTION_ORGANIZATION_MISMATCH", Assert.IsType<CommercialContractError>(Assert.IsType<ObjectResult>(crossOrganization.Result).Value).Code);

        var valid = await controller.Create(Request(otherAccount.Id, subscription.Id), default);
        Assert.IsType<CreatedAtActionResult>(valid.Result);
        Assert.True(await billingDb.CommercialContracts.AnyAsync(x => x.SubscriptionId == subscription.Id && x.AccountId == otherAccount.Id));
    }

    private static CommercialContract Contract(Guid accountId, DateTime now, bool visible) =>
        new(accountId, null, $"REF-{Guid.NewGuid():N}", Guid.NewGuid(), "Aprovação comercial", now, null,
            [new CommercialTermDefinition("condition", "custom", "text", "value", now, VisibleToCustomer: visible)], now);
    private static CommercialTermDefinition Term(string value, DateTime from) =>
        new("discount", "negotiated", "decimal", value, from);
    private static CreateCommercialContractRequest Request(Guid accountId, Guid subscriptionId)
    {
        var now = DateTime.UtcNow;
        return new(accountId, subscriptionId, $"REF-{Guid.NewGuid():N}", "Validação de vínculo", now, null,
            [new CommercialTermInput("condition", "custom", "text", "value", now)]);
    }

    private sealed class TestUser(Guid id) : ICurrentUser { public Guid? UserId => id; public bool IsAuthenticated => true; }

    private sealed class TestTenant(Guid unitId) : ICurrentTenant
    {
        public Guid? TenantId => unitId;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }
}
