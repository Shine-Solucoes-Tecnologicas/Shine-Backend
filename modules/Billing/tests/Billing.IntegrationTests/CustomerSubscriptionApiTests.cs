using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Billing.Application;
using Billing.Domain;
using Billing.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Shine.Api.Controllers;
using Shine.Domain;
using Shine.Domain.Authorization;
using Shine.Domain.Identity;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence.Seed;

namespace Shine.IntegrationTests;

[Collection("database")]
public sealed class CustomerSubscriptionApiTests(DatabaseFixture fixture)
{
    private const string JwtSecret = "customer-subscription-tests-secret-with-at-least-32-bytes";

    [Fact]
    public async Task Customer_financial_api_is_scoped_audited_provider_neutral_and_denies_operational_and_platform_users()
    {
        var setup = await SetupAsync();
        var provider = new TestBillingProvider();
        await using var factory = new ApiFactory(ConnectionString(), JwtSecret, provider);
        using var client = factory.CreateClient();

        client.DefaultRequestHeaders.Authorization = Bearer(setup.Editor.Id, setup.Unit.Id, setup.EditorMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/account/billing/subscriptions/checkout",
            CheckoutBody(setup.Plan.Id, setup.CheckoutUnit.Id, "denied-editor"))).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(setup.PlatformUser.Id, setup.Unit.Id, setup.PlatformMembership.UserTenantId);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/account/billing/subscriptions/checkout",
            CheckoutBody(setup.Plan.Id, setup.CheckoutUnit.Id, "denied-platform"))).StatusCode);

        client.DefaultRequestHeaders.Authorization = Bearer(setup.Financial.Id, setup.Unit.Id, setup.FinancialMembership.UserTenantId);
        var checkout = await client.PostAsJsonAsync("/api/account/billing/subscriptions/checkout",
            CheckoutBody(setup.Plan.Id, setup.CheckoutUnit.Id, setup.IdempotencyKey));
        Assert.Equal(HttpStatusCode.Created, checkout.StatusCode);
        var checkoutResponse = await checkout.Content.ReadFromJsonAsync<SubscriptionCheckoutResponse>();
        Assert.NotNull(checkoutResponse);
        Assert.Equal(SubscriptionStatus.Draft, checkoutResponse.Status);
        Assert.Equal("TEST", checkoutResponse.ProviderCode);

        var retry = await client.PostAsJsonAsync("/api/account/billing/subscriptions/checkout",
            CheckoutBody(setup.Plan.Id, setup.CheckoutUnit.Id, setup.IdempotencyKey));
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(checkoutResponse.SubscriptionId, (await retry.Content.ReadFromJsonAsync<SubscriptionCheckoutResponse>())!.SubscriptionId);

        var conflict = await client.PostAsJsonAsync("/api/account/billing/subscriptions/checkout",
            CheckoutBody(setup.OtherPlan.Id, setup.CheckoutUnit.Id, setup.IdempotencyKey));
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IDEMPOTENCY_CONFLICT", (await conflict.Content.ReadFromJsonAsync<CustomerBillingError>())!.Code);

        var change = await client.PostAsJsonAsync($"/api/account/billing/subscriptions/{setup.ActiveSubscriptionId}/plan-change",
            new { planId = setup.OtherPlan.Id, effectiveAtUtc = setup.ActivePeriodEnd });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);

        var cancel = await client.PostAsJsonAsync($"/api/account/billing/subscriptions/{setup.ActiveSubscriptionId}/cancellation",
            new { atPeriodEnd = true });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        Assert.Equal(SubscriptionStatus.CancellationPending,
            (await cancel.Content.ReadFromJsonAsync<CustomerSubscriptionResponse>())!.Status);

        var subscriptions = await client.GetFromJsonAsync<CustomerSubscriptionResponse[]>("/api/account/billing/subscriptions");
        Assert.Contains(subscriptions!, x => x.Id == checkoutResponse.SubscriptionId);
        Assert.Contains(subscriptions!, x => x.Id == setup.ActiveSubscriptionId);
        var invoices = await client.GetFromJsonAsync<CustomerInvoiceResponse[]>("/api/account/billing/invoices");
        Assert.Contains(invoices!, x => x.Id == setup.InvoiceId);
        Assert.DoesNotContain(invoices!, x => x.Id == setup.ForeignInvoiceId);

        await using var core = fixture.CreateDb();
        var actions = await core.AuditEntries.Where(x => x.EntityType == "CustomerSubscription" && x.UserId == setup.Financial.Id)
            .Select(x => x.Action).ToArrayAsync();
        Assert.Contains("CHECKOUT_STARTED", actions);
        Assert.Contains("PLAN_CHANGE_REQUESTED", actions);
        Assert.Contains("CANCELLATION_REQUESTED", actions);
        Assert.Equal(2, provider.CheckoutCalls);
    }

    [Fact]
    public async Task Checkout_returns_stable_error_when_provider_is_not_registered()
    {
        var setup = await SetupAsync();
        await using var factory = new ApiFactory(ConnectionString(), JwtSecret, null);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = Bearer(setup.Financial.Id, setup.Unit.Id, setup.FinancialMembership.UserTenantId);

        var response = await client.PostAsJsonAsync("/api/account/billing/subscriptions/checkout",
            CheckoutBody(setup.Plan.Id, setup.CheckoutUnit.Id, $"missing-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("BILLING_PROVIDER_UNAVAILABLE", (await response.Content.ReadFromJsonAsync<CustomerBillingError>())!.Code);
    }

    private async Task<Setup> SetupAsync()
    {
        var account = new CustomerAccount($"Billing API {Guid.NewGuid():N}");
        var foreignAccount = new CustomerAccount($"Foreign Billing API {Guid.NewGuid():N}");
        var unit = new Tenant($"Billing API unit {Guid.NewGuid():N}");
        var checkoutUnit = new Tenant($"Billing checkout unit {Guid.NewGuid():N}");
        var foreignUnit = new Tenant($"Foreign billing unit {Guid.NewGuid():N}");
        unit.AssignToCustomerAccount(account.Id); checkoutUnit.AssignToCustomerAccount(account.Id); foreignUnit.AssignToCustomerAccount(foreignAccount.Id);
        var financial = new User($"financial-{Guid.NewGuid():N}@example.test", "hash");
        var editor = new User($"editor-{Guid.NewGuid():N}@example.test", "hash");
        var platform = new User($"platform-{Guid.NewGuid():N}@example.test", "hash");
        var financialMembership = new UserTenant(financial.Id, unit.Id, financial.Id, false);
        var financialCheckoutMembership = new UserTenant(financial.Id, checkoutUnit.Id, financial.Id, false);
        var editorMembership = new UserTenant(editor.Id, unit.Id, editor.Id, false);
        var platformMembership = new UserTenant(platform.Id, unit.Id, platform.Id, false);
        var plan = new Plan($"BILLING-{Guid.NewGuid():N}", "Billing API plan");
        var otherPlan = new Plan($"BILLING-NEXT-{Guid.NewGuid():N}", "Billing API next plan");

        await using (var core = fixture.CreateDb())
        {
            core.AddRange(account, foreignAccount, unit, checkoutUnit, foreignUnit, financial, editor, platform,
                financialMembership, financialCheckoutMembership, editorMembership, platformMembership, plan, otherPlan,
                new CustomerAccountUser(account.Id, financial.Id), new CustomerAccountUser(account.Id, editor.Id));
            await core.SaveChangesAsync();
            await AuthorizationSeed.SeedCustomerAccountDefaultsAsync(core, account.Id, financial.Id);
            var editorRole = await core.CustomerAccountRoles.SingleAsync(x => x.AccountId == account.Id && x.Name == CustomerAccountRole.EditorName);
            core.CustomerAccountUserRoles.Add(new CustomerAccountUserRole(account.Id, editor.Id, editorRole.Id, true, true));
            await AuthorizationSeed.EnsureGlobalRolesAsync(core);
            var platformRole = await core.GlobalRoles.SingleAsync(x => x.Name == GlobalRole.PlatformAdminName);
            core.UserGlobalRoles.Add(new UserGlobalRole(platform.Id, platformRole.Id));
            await core.SaveChangesAsync();
        }

        var start = DateTime.UtcNow;
        Guid activeSubscriptionId;
        Guid invoiceId;
        Guid foreignInvoiceId;
        await using (var billing = fixture.CreateBillingDb())
        {
            var active = new Subscription(account.Id, plan.Id, new BillingInterval(BillingIntervalUnit.Month, 1));
            active.AddUnit(unit.Id); active.Activate(start); billing.Subscriptions.Add(active);
            var foreign = new Subscription(foreignAccount.Id, plan.Id, new BillingInterval(BillingIntervalUnit.Month, 1));
            foreign.AddUnit(foreignUnit.Id); foreign.Activate(start); billing.Subscriptions.Add(foreign);
            await billing.SaveChangesAsync();
            var ledger = new FinancialLedgerService(new FinancialRecordRepository(billing));
            invoiceId = (await ledger.CreateInvoiceAsync(account.Id, active.Id, $"invoice-{Guid.NewGuid():N}", 100, "BRL",
                start, start.AddMonths(1), start.AddDays(5), start)).Id;
            foreignInvoiceId = (await ledger.CreateInvoiceAsync(foreignAccount.Id, foreign.Id, $"invoice-{Guid.NewGuid():N}", 200, "BRL",
                start, start.AddMonths(1), start.AddDays(5), start)).Id;
            activeSubscriptionId = active.Id;
        }

        return new(account, unit, checkoutUnit, financial, editor, platform, financialMembership, editorMembership,
            platformMembership, plan, otherPlan, activeSubscriptionId, start.AddMonths(1), invoiceId, foreignInvoiceId,
            $"checkout-{Guid.NewGuid():N}");
    }

    private static object CheckoutBody(Guid planId, Guid unitId, string key) => new
    {
        planId, unitIds = new[] { unitId }, intervalUnit = BillingIntervalUnit.Month, intervalCount = 1,
        providerCode = "TEST", successUrl = "https://app.example.test/success", cancelUrl = "https://app.example.test/cancel",
        idempotencyKey = key
    };

    private static AuthenticationHeaderValue Bearer(Guid userId, Guid tenantId, Guid userTenantId)
    {
        var options = Options.Create(new JwtOptions { Secret = JwtSecret, Issuer = "Shine", Audience = "Shine.Api", AccessTokenMinutes = 5 });
        return new AuthenticationHeaderValue("Bearer", new JwtAccessTokenService(options, new JwtSigningKeyRing(options))
            .Create(userId, tenantId, userTenantId, []).Token);
    }

    private string ConnectionString() => fixture.ConnectionString;

    private sealed class ApiFactory(string connectionString, string jwtSecret, TestBillingProvider? provider) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:ShineDb", connectionString);
            builder.UseSetting("Jwt:Secret", jwtSecret); builder.UseSetting("Jwt:Issuer", "Shine"); builder.UseSetting("Jwt:Audience", "Shine.Api");
            builder.UseSetting("RabbitMq:Enabled", "false");
            builder.ConfigureLogging(logging => { logging.ClearProviders(); logging.AddJsonConsole(); });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                if (provider is not null) services.AddSingleton<IBillingProvider>(provider);
            });
        }
    }

    private sealed class TestBillingProvider : IBillingProvider
    {
        public string ProviderCode => "TEST";
        public int CheckoutCalls { get; private set; }
        public Task<ProviderCheckoutResult> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken cancellationToken = default)
        {
            CheckoutCalls++;
            return Task.FromResult(new ProviderCheckoutResult($"external-{request.SubscriptionId:N}", new Uri($"https://pay.example.test/{request.SubscriptionId:N}")));
        }
        public Task<ProviderChargeResult> CreateChargeAsync(ProviderChargeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task CancelAsync(string externalSubscriptionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ProviderSubscriptionState> GetSubscriptionAsync(string externalSubscriptionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed record Setup(CustomerAccount Account, Tenant Unit, Tenant CheckoutUnit, User Financial, User Editor,
        User PlatformUser, UserTenant FinancialMembership, UserTenant EditorMembership, UserTenant PlatformMembership,
        Plan Plan, Plan OtherPlan, Guid ActiveSubscriptionId, DateTime ActivePeriodEnd, Guid InvoiceId, Guid ForeignInvoiceId,
        string IdempotencyKey);
}
