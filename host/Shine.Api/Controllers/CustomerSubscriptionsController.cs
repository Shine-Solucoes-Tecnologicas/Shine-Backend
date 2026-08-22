using System.Text.Json;
using Billing.Application;
using Billing.Domain;
using Billing.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Domain;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/account/billing")]
public sealed class CustomerSubscriptionsController(
    BillingDbContext billingDb,
    ShineDbContext coreDb,
    ICurrentUser currentUser,
    ICurrentTenant currentTenant,
    IPermissionAuthorization permissions,
    ISubscriptionUnitOwnership ownership,
    ISubscriptionPlanCatalog plans,
    IEnumerable<IBillingProvider> providers) : ControllerBase
{
    [HttpGet("subscriptions")]
    [RequiresPermission("subscriptions.read")]
    public async Task<ActionResult<IReadOnlyCollection<CustomerSubscriptionResponse>>> Subscriptions(CancellationToken cancellationToken)
    {
        var context = await ContextAsync(cancellationToken);
        if (context is null) return Forbid();
        var subscriptions = await billingDb.Subscriptions.AsNoTracking().Include(x => x.Units)
            .Where(x => x.AccountId == context.Value.AccountId)
            .OrderByDescending(x => x.CreatedAtUtc).ToArrayAsync(cancellationToken);
        var visible = new List<CustomerSubscriptionResponse>();
        foreach (var subscription in subscriptions)
            if (await HasPermissionOnAllUnitsAsync(context.Value.UserId, subscription.UnitIds, "subscriptions.read", cancellationToken))
                visible.Add(CustomerSubscriptionResponse.From(subscription));
        return Ok(visible);
    }

    [HttpGet("subscriptions/{subscriptionId:guid}")]
    [RequiresPermission("subscriptions.read")]
    public async Task<ActionResult<CustomerSubscriptionResponse>> Subscription(Guid subscriptionId, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(cancellationToken);
        if (context is null) return Forbid();
        var subscription = await billingDb.Subscriptions.AsNoTracking().Include(x => x.Units)
            .SingleOrDefaultAsync(x => x.Id == subscriptionId && x.AccountId == context.Value.AccountId, cancellationToken);
        if (subscription is null ||
            !await HasPermissionOnAllUnitsAsync(context.Value.UserId, subscription.UnitIds, "subscriptions.read", cancellationToken))
            return Error(StatusCodes.Status404NotFound, "SUBSCRIPTION_NOT_FOUND", "Assinatura não encontrada.");
        return Ok(CustomerSubscriptionResponse.From(subscription));
    }

    [HttpGet("invoices")]
    [RequiresPermission("billing.read")]
    public async Task<ActionResult<IReadOnlyCollection<CustomerInvoiceResponse>>> Invoices(CancellationToken cancellationToken)
    {
        var context = await ContextAsync(cancellationToken);
        if (context is null) return Forbid();
        var subscriptions = await billingDb.Subscriptions.AsNoTracking().Include(x => x.Units)
            .Where(x => x.AccountId == context.Value.AccountId).ToArrayAsync(cancellationToken);
        var visibleSubscriptionIds = new List<Guid>();
        foreach (var subscription in subscriptions)
            if (await HasPermissionOnAllUnitsAsync(context.Value.UserId, subscription.UnitIds, "billing.read", cancellationToken))
                visibleSubscriptionIds.Add(subscription.Id);
        var invoices = await billingDb.Invoices.AsNoTracking()
            .Where(x => x.AccountId == context.Value.AccountId && visibleSubscriptionIds.Contains(x.SubscriptionId))
            .OrderByDescending(x => x.CreatedAtUtc).ToArrayAsync(cancellationToken);
        return Ok(invoices.Select(CustomerInvoiceResponse.From).ToArray());
    }

    [HttpPost("subscriptions/checkout")]
    public async Task<ActionResult<SubscriptionCheckoutResponse>> Checkout(
        StartSubscriptionCheckoutRequest request, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(cancellationToken);
        if (context is null) return Forbid();
        var unitIds = request.UnitIds?.Distinct().ToArray() ?? [];
        var permission = await ManagementPermissionAsync(context.Value.UserId, unitIds, cancellationToken);
        if (permission is null) return Forbid();
        if (unitIds.Length == 0)
            return Error(StatusCodes.Status400BadRequest, "UNITS_REQUIRED", "Informe ao menos uma unidade para a assinatura.");
        if (!ValidCallback(request.SuccessUrl) || !ValidCallback(request.CancelUrl))
            return Error(StatusCodes.Status400BadRequest, "INVALID_CHECKOUT_CALLBACK", "As URLs de retorno do checkout devem usar HTTPS.");
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey) || request.IdempotencyKey.Trim().Length > 120)
            return Error(StatusCodes.Status400BadRequest, "INVALID_IDEMPOTENCY_KEY", "Informe uma chave de idempotência válida.");

        var provider = providers.SingleOrDefault(x => string.Equals(x.ProviderCode, request.ProviderCode, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
            return Error(StatusCodes.Status503ServiceUnavailable, "BILLING_PROVIDER_UNAVAILABLE", "O meio de pagamento solicitado não está disponível.");
        if (!await plans.ExistsAsync(request.PlanId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PLAN_NOT_FOUND", "Plano não encontrado.");
        if (!await ownership.AllBelongToAccountAsync(context.Value.AccountId, unitIds, cancellationToken))
            return Error(StatusCodes.Status400BadRequest, "UNIT_OUT_OF_SCOPE", "Uma ou mais unidades não pertencem à organização.");

        var idempotencyKey = request.IdempotencyKey.Trim();
        var subscription = await billingDb.Subscriptions.Include(x => x.Units).SingleOrDefaultAsync(x =>
            x.AccountId == context.Value.AccountId && x.CheckoutIdempotencyKey == idempotencyKey, cancellationToken);
        var created = subscription is null;
        if (subscription is null)
        {
            if (await billingDb.SubscriptionUnits.AnyAsync(x => unitIds.Contains(x.UnitId) && x.IsEffective, cancellationToken))
                return Error(StatusCodes.Status409Conflict, "UNIT_ALREADY_SUBSCRIBED", "Uma ou mais unidades já possuem uma assinatura ativa.");
            try
            {
                subscription = new Subscription(context.Value.AccountId, request.PlanId,
                    new BillingInterval(request.IntervalUnit, request.IntervalCount));
                foreach (var unitId in unitIds) subscription.AddUnit(unitId);
                subscription.PrepareCheckout(provider.ProviderCode, idempotencyKey);
                billingDb.Subscriptions.Add(subscription);
                await billingDb.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is DomainException or ArgumentException or ArgumentOutOfRangeException)
            {
                return Error(StatusCodes.Status400BadRequest, "INVALID_SUBSCRIPTION", exception.Message);
            }
        }
        else if (!CheckoutMatches(subscription, request, provider.ProviderCode, unitIds))
        {
            return Error(StatusCodes.Status409Conflict, "IDEMPOTENCY_CONFLICT", "A chave de idempotência já foi usada com dados diferentes.");
        }

        try
        {
            var checkout = await provider.CreateCheckoutAsync(new ProviderCheckoutRequest(
                subscription.Id, subscription.AccountId, subscription.PlanId, request.SuccessUrl, request.CancelUrl, idempotencyKey), cancellationToken);
            subscription.AttachExternalSubscription(provider.ProviderCode, checkout.ExternalSubscriptionId);
            await billingDb.SaveChangesAsync(cancellationToken);
            AddAudit(context.Value, subscription.Id, "CHECKOUT_STARTED", new { subscription.PlanId, UnitIds = unitIds, Provider = provider.ProviderCode });
            await coreDb.SaveChangesAsync(cancellationToken);
            var response = new SubscriptionCheckoutResponse(subscription.Id, subscription.Status, checkout.CheckoutUrl, provider.ProviderCode);
            return created ? Created($"/api/account/billing/subscriptions/{subscription.Id}", response) : Ok(response);
        }
        catch (TransientBillingProviderException)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "BILLING_PROVIDER_TEMPORARILY_UNAVAILABLE", "O meio de pagamento está temporariamente indisponível.");
        }
        catch (DomainException exception)
        {
            return Error(StatusCodes.Status409Conflict, "CHECKOUT_CONFLICT", exception.Message);
        }
    }

    [HttpPost("subscriptions/{subscriptionId:guid}/plan-change")]
    public async Task<ActionResult<CustomerSubscriptionResponse>> ChangePlan(
        Guid subscriptionId, ChangeCustomerSubscriptionPlanRequest request, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(cancellationToken);
        if (context is null) return Forbid();
        var subscription = await billingDb.Subscriptions.Include(x => x.Units)
            .SingleOrDefaultAsync(x => x.Id == subscriptionId && x.AccountId == context.Value.AccountId, cancellationToken);
        if (subscription is null) return Error(StatusCodes.Status404NotFound, "SUBSCRIPTION_NOT_FOUND", "Assinatura não encontrada.");
        if (await ManagementPermissionAsync(context.Value.UserId, subscription.UnitIds, cancellationToken) is null) return Forbid();
        if (!await plans.ExistsAsync(request.PlanId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "PLAN_NOT_FOUND", "Plano não encontrado.");
        try
        {
            subscription.RequestPlanChange(request.PlanId, request.EffectiveAtUtc);
            await billingDb.SaveChangesAsync(cancellationToken);
            AddAudit(context.Value, subscription.Id, "PLAN_CHANGE_REQUESTED", new { request.PlanId, request.EffectiveAtUtc });
            await coreDb.SaveChangesAsync(cancellationToken);
            return Ok(CustomerSubscriptionResponse.From(subscription));
        }
        catch (Exception exception) when (exception is DomainException or ArgumentException)
        {
            return Error(StatusCodes.Status409Conflict, "PLAN_CHANGE_NOT_ALLOWED", exception.Message);
        }
    }

    [HttpPost("subscriptions/{subscriptionId:guid}/cancellation")]
    public async Task<ActionResult<CustomerSubscriptionResponse>> Cancel(
        Guid subscriptionId, CancelCustomerSubscriptionRequest request, CancellationToken cancellationToken)
    {
        var context = await ContextAsync(cancellationToken);
        if (context is null) return Forbid();
        var subscription = await billingDb.Subscriptions.Include(x => x.Units)
            .SingleOrDefaultAsync(x => x.Id == subscriptionId && x.AccountId == context.Value.AccountId, cancellationToken);
        if (subscription is null) return Error(StatusCodes.Status404NotFound, "SUBSCRIPTION_NOT_FOUND", "Assinatura não encontrada.");
        if (await ManagementPermissionAsync(context.Value.UserId, subscription.UnitIds, cancellationToken) is null) return Forbid();
        try
        {
            if (subscription.ProviderCode is not null && subscription.ExternalSubscriptionId is not null)
            {
                var provider = providers.SingleOrDefault(x => string.Equals(x.ProviderCode, subscription.ProviderCode, StringComparison.OrdinalIgnoreCase));
                if (provider is null)
                    return Error(StatusCodes.Status503ServiceUnavailable, "BILLING_PROVIDER_UNAVAILABLE", "O meio de pagamento da assinatura não está disponível.");
                await provider.CancelAsync(subscription.ExternalSubscriptionId, cancellationToken);
            }
            subscription.RequestCancellation(request.AtPeriodEnd, DateTime.UtcNow);
            await billingDb.SaveChangesAsync(cancellationToken);
            AddAudit(context.Value, subscription.Id, "CANCELLATION_REQUESTED", new { request.AtPeriodEnd, subscription.CancellationEffectiveAtUtc });
            await coreDb.SaveChangesAsync(cancellationToken);
            return Ok(CustomerSubscriptionResponse.From(subscription));
        }
        catch (TransientBillingProviderException)
        {
            return Error(StatusCodes.Status503ServiceUnavailable, "BILLING_PROVIDER_TEMPORARILY_UNAVAILABLE", "O meio de pagamento está temporariamente indisponível.");
        }
        catch (Exception exception) when (exception is DomainException or ArgumentException)
        {
            return Error(StatusCodes.Status409Conflict, "CANCELLATION_NOT_ALLOWED", exception.Message);
        }
    }

    private async Task<(Guid AccountId, Guid UserId, Guid UnitId)?> ContextAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid userId || currentTenant.TenantId is not Guid unitId ||
            await permissions.IsPlatformOperatorAsync(userId, cancellationToken)) return null;
        var accountId = await coreDb.Tenants.AsNoTracking().Where(x => x.Id == unitId && x.IsActive)
            .Select(x => x.CustomerAccountId).SingleOrDefaultAsync(cancellationToken);
        return accountId is Guid value ? (value, userId, unitId) : null;
    }

    private async Task<string?> ManagementPermissionAsync(Guid userId, IReadOnlyCollection<Guid> unitIds, CancellationToken cancellationToken)
    {
        foreach (var permission in new[] { "subscriptions.manage", "billing.manage" })
            if (unitIds.Count > 0 && (await Task.WhenAll(unitIds.Select(unitId =>
                    permissions.HasPermissionAsync(userId, unitId, permission, cancellationToken)))).All(x => x))
                return permission;
        return null;
    }

    private static bool CheckoutMatches(Subscription subscription, StartSubscriptionCheckoutRequest request,
        string providerCode, IReadOnlyCollection<Guid> unitIds) =>
        subscription.Status == SubscriptionStatus.Draft && subscription.PlanId == request.PlanId &&
        subscription.Interval.Unit == request.IntervalUnit && subscription.Interval.Count == request.IntervalCount &&
        string.Equals(subscription.ProviderCode, providerCode, StringComparison.OrdinalIgnoreCase) &&
        subscription.UnitIds.SetEquals(unitIds);

    private static bool ValidCallback(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    private async Task<bool> HasPermissionOnAllUnitsAsync(Guid userId, IReadOnlyCollection<Guid> unitIds,
        string permission, CancellationToken cancellationToken) =>
        unitIds.Count > 0 && (await Task.WhenAll(unitIds.Select(unitId =>
            permissions.HasPermissionAsync(userId, unitId, permission, cancellationToken)))).All(x => x);

    private void AddAudit((Guid AccountId, Guid UserId, Guid UnitId) context, Guid subscriptionId, string action, object values) =>
        coreDb.AuditEntries.Add(AuditEntry.Create("CustomerSubscription", subscriptionId.ToString(), action,
            context.UserId, context.UnitId, DateTime.UtcNow, HttpContext.TraceIdentifier,
            HttpContext.Connection.RemoteIpAddress?.ToString(), HttpContext.Request.Headers.UserAgent.ToString(),
            newValuesJson: JsonSerializer.Serialize(values)));

    private ObjectResult Error(int statusCode, string code, string message) =>
        StatusCode(statusCode, new CustomerBillingError(code, message));
}

public sealed record StartSubscriptionCheckoutRequest(Guid PlanId, IReadOnlyCollection<Guid>? UnitIds,
    BillingIntervalUnit IntervalUnit, int IntervalCount, string ProviderCode, string SuccessUrl, string CancelUrl, string IdempotencyKey);
public sealed record SubscriptionCheckoutResponse(Guid SubscriptionId, SubscriptionStatus Status, Uri CheckoutUrl, string ProviderCode);
public sealed record ChangeCustomerSubscriptionPlanRequest(Guid PlanId, DateTime EffectiveAtUtc);
public sealed record CancelCustomerSubscriptionRequest(bool AtPeriodEnd);
public sealed record CustomerBillingError(string Code, string Message);
public sealed record CustomerSubscriptionResponse(Guid Id, Guid PlanId, Guid? PendingPlanId, SubscriptionStatus Status,
    BillingIntervalUnit IntervalUnit, int IntervalCount, IReadOnlyCollection<Guid> UnitIds, DateTime? CurrentPeriodStartsAtUtc,
    DateTime? CurrentPeriodEndsAtUtc, DateTime? CancellationEffectiveAtUtc)
{
    public static CustomerSubscriptionResponse From(Subscription subscription) => new(subscription.Id, subscription.PlanId,
        subscription.PendingPlanId, subscription.Status, subscription.Interval.Unit, subscription.Interval.Count,
        subscription.UnitIds.Order().ToArray(), subscription.CurrentPeriodStartsAtUtc, subscription.CurrentPeriodEndsAtUtc,
        subscription.CancellationEffectiveAtUtc);
}
public sealed record CustomerInvoiceResponse(Guid Id, Guid SubscriptionId, decimal Amount, string Currency,
    InvoiceStatus Status, DateTime CycleStartsAtUtc, DateTime CycleEndsAtUtc, DateTime DueAtUtc, DateTime CreatedAtUtc)
{
    public static CustomerInvoiceResponse From(Invoice invoice) => new(invoice.Id, invoice.SubscriptionId, invoice.Amount,
        invoice.Currency, invoice.Status, invoice.CycleStartsAtUtc, invoice.CycleEndsAtUtc, invoice.DueAtUtc, invoice.CreatedAtUtc);
}
