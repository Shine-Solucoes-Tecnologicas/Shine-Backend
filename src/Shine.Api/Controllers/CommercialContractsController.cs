using System.Data;
using System.Text.Json;
using Billing.Domain;
using Billing.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shine.Infrastructure;
using Shine.Infrastructure.Persistence;

namespace Shine.Api.Controllers;

[ApiController]
[Route("api/admin/billing/contracts")]
[RequiresGlobalPermission("billing.commercial.read")]
public sealed class AdministrativeCommercialContractsController(
    BillingDbContext billingDb,
    ShineDbContext coreDb,
    ICurrentUser currentUser) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CommercialContractResponse>>> List(
        [FromQuery] Guid? accountId, CancellationToken cancellationToken)
    {
        var query = billingDb.CommercialContracts.AsNoTracking();
        if (accountId is not null) query = query.Where(x => x.AccountId == accountId);
        var contracts = await query.Include(x => x.Revisions).ThenInclude(x => x.Terms)
            .OrderByDescending(x => x.UpdatedAtUtc).ToArrayAsync(cancellationToken);
        return Ok(contracts.Select(x => CommercialContractResponse.From(x, includeInternal: true)).ToArray());
    }

    [HttpGet("{contractId:guid}/history")]
    public async Task<ActionResult<IReadOnlyCollection<CommercialContractRevisionResponse>>> History(Guid contractId, CancellationToken cancellationToken)
    {
        var contract = await billingDb.CommercialContracts.AsNoTracking()
            .Include(x => x.Revisions).ThenInclude(x => x.Terms)
            .SingleOrDefaultAsync(x => x.Id == contractId, cancellationToken);
        return contract is null
            ? NotFound()
            : Ok(contract.Revisions.OrderByDescending(x => x.RevisionNumber).Select(x => CommercialContractRevisionResponse.From(x, true)).ToArray());
    }

    [HttpPost]
    [RequiresGlobalPermission("billing.commercial.manage")]
    public async Task<ActionResult<CommercialContractResponse>> Create(CreateCommercialContractRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid actorUserId) return Unauthorized();
        if (!await coreDb.CustomerAccounts.AsNoTracking().AnyAsync(x => x.Id == request.AccountId, cancellationToken))
            return Error(StatusCodes.Status404NotFound, "ORGANIZATION_NOT_FOUND", "Organização não encontrada.");
        if (request.SubscriptionId is Guid subscriptionId)
        {
            var subscriptionAccountId = await billingDb.Subscriptions.AsNoTracking()
                .Where(x => x.Id == subscriptionId).Select(x => (Guid?)x.AccountId).SingleOrDefaultAsync(cancellationToken);
            if (subscriptionAccountId is null)
                return Error(StatusCodes.Status404NotFound, "SUBSCRIPTION_NOT_FOUND", "Assinatura não encontrada.");
            if (subscriptionAccountId != request.AccountId)
                return Error(StatusCodes.Status409Conflict, "SUBSCRIPTION_ORGANIZATION_MISMATCH", "A assinatura não pertence à organização do contrato.");
        }
        if (string.IsNullOrWhiteSpace(request.Reference))
            return Error(StatusCodes.Status400BadRequest, "INVALID_COMMERCIAL_CONTRACT", "A referência do contrato é obrigatória.");
        var normalizedReference = request.Reference.Trim();
        if (await billingDb.CommercialContracts.AnyAsync(x => x.AccountId == request.AccountId && x.Reference == normalizedReference, cancellationToken))
            return Error(StatusCodes.Status409Conflict, "CONTRACT_REFERENCE_EXISTS", "Já existe um contrato com esta referência para a organização.");

        try
        {
            var contract = new CommercialContract(request.AccountId, request.SubscriptionId, normalizedReference, actorUserId,
                request.Justification, request.ValidFromUtc, request.ValidUntilUtc,
                request.Terms?.Select(x => x.ToDefinition()) ?? [], DateTime.UtcNow);
            billingDb.CommercialContracts.Add(contract);
            await billingDb.SaveChangesAsync(cancellationToken);
            return CreatedAtAction(nameof(History), new { contractId = contract.Id }, CommercialContractResponse.From(contract, true));
        }
        catch (Exception exception) when (exception is Shine.Domain.DomainException or ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_COMMERCIAL_CONTRACT", exception.Message);
        }
    }

    [HttpPost("{contractId:guid}/revisions")]
    [RequiresGlobalPermission("billing.commercial.manage")]
    public async Task<ActionResult<CommercialContractResponse>> Amend(Guid contractId, AmendCommercialContractRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid actorUserId) return Unauthorized();
        await using var transaction = await billingDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var contract = await billingDb.CommercialContracts.Include(x => x.Revisions).ThenInclude(x => x.Terms)
            .SingleOrDefaultAsync(x => x.Id == contractId, cancellationToken);
        if (contract is null) return NotFound();
        try
        {
            contract.Amend(actorUserId, request.Justification, request.ValidFromUtc, request.ValidUntilUtc,
                request.Terms?.Select(x => x.ToDefinition()) ?? [], DateTime.UtcNow);
            await billingDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Ok(CommercialContractResponse.From(contract, true));
        }
        catch (Exception exception) when (exception is Shine.Domain.DomainException or ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_COMMERCIAL_CONTRACT", exception.Message);
        }
    }

    [HttpPost("{contractId:guid}/end")]
    [RequiresGlobalPermission("billing.commercial.manage")]
    public async Task<IActionResult> End(Guid contractId, EndCommercialContractRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not Guid actorUserId) return Unauthorized();
        await using var transaction = await billingDb.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var contract = await billingDb.CommercialContracts.Include(x => x.Revisions).ThenInclude(x => x.Terms)
            .SingleOrDefaultAsync(x => x.Id == contractId, cancellationToken);
        if (contract is null) return NotFound();
        try
        {
            contract.End(actorUserId, request.Justification, request.EndedAtUtc, DateTime.UtcNow);
            await billingDb.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return NoContent();
        }
        catch (Exception exception) when (exception is Shine.Domain.DomainException or ArgumentException)
        {
            return Error(StatusCodes.Status400BadRequest, "INVALID_COMMERCIAL_CONTRACT", exception.Message);
        }
    }

    private ObjectResult Error(int status, string code, string message) => StatusCode(status, new CommercialContractError(code, message));
}

[ApiController]
[Route("api/account/billing/contracts")]
[RequiresPermission("billing.read")]
public sealed class CustomerCommercialContractsController(
    BillingDbContext billingDb,
    ShineDbContext coreDb,
    ICurrentTenant currentTenant) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<CommercialContractResponse>>> List(CancellationToken cancellationToken)
    {
        if (currentTenant.TenantId is not Guid unitId) return Forbid();
        var accountId = await coreDb.Tenants.AsNoTracking().Where(x => x.Id == unitId)
            .Select(x => x.CustomerAccountId).SingleOrDefaultAsync(cancellationToken);
        if (accountId is not Guid organizationId) return Forbid();

        var contracts = await billingDb.CommercialContracts.AsNoTracking()
            .Where(x => x.AccountId == organizationId)
            .Include(x => x.Revisions).ThenInclude(x => x.Terms)
            .OrderByDescending(x => x.UpdatedAtUtc).ToArrayAsync(cancellationToken);
        return Ok(contracts.Select(x => CommercialContractResponse.From(x, includeInternal: false)).ToArray());
    }
}

public sealed record CreateCommercialContractRequest(Guid AccountId, Guid? SubscriptionId, string Reference, string Justification,
    DateTime ValidFromUtc, DateTime? ValidUntilUtc, IReadOnlyCollection<CommercialTermInput> Terms);
public sealed record AmendCommercialContractRequest(string Justification, DateTime ValidFromUtc, DateTime? ValidUntilUtc,
    IReadOnlyCollection<CommercialTermInput> Terms);
public sealed record EndCommercialContractRequest(string Justification, DateTime EndedAtUtc);
public sealed record CommercialTermInput(string Category, string Code, string ValueType, string Value, DateTime ValidFromUtc,
    DateTime? ValidUntilUtc = null, bool VisibleToCustomer = true, IReadOnlyDictionary<string, string>? Parameters = null)
{
    public CommercialTermDefinition ToDefinition() => new(Category, Code, ValueType, Value, ValidFromUtc, ValidUntilUtc, VisibleToCustomer, Parameters);
}
public sealed record CommercialContractError(string Code, string Message);
public sealed record CommercialContractResponse(Guid Id, Guid AccountId, Guid? SubscriptionId, string Reference,
    CommercialContractStatus Status, int CurrentRevisionNumber, CommercialContractRevisionResponse CurrentRevision)
{
    public static CommercialContractResponse From(CommercialContract contract, bool includeInternal)
    {
        var revision = contract.Revisions.Single(x => x.RevisionNumber == contract.CurrentRevisionNumber);
        return new(contract.Id, contract.AccountId, contract.SubscriptionId, contract.Reference, contract.Status,
            contract.CurrentRevisionNumber, CommercialContractRevisionResponse.From(revision, includeInternal));
    }
}
public sealed record CommercialContractRevisionResponse(int RevisionNumber, CommercialContractStatus Status, Guid? ActorUserId,
    string? Justification, DateTime ValidFromUtc, DateTime? ValidUntilUtc, DateTime CreatedAtUtc,
    IReadOnlyCollection<CommercialTermResponse> Terms)
{
    public static CommercialContractRevisionResponse From(CommercialContractRevision revision, bool includeInternal) =>
        new(revision.RevisionNumber, revision.Status, includeInternal ? revision.ActorUserId : null,
            includeInternal ? revision.Justification : null, revision.ValidFromUtc, revision.ValidUntilUtc, revision.CreatedAtUtc,
            revision.Terms.Where(x => includeInternal || x.VisibleToCustomer).Select(CommercialTermResponse.From).ToArray());
}
public sealed record CommercialTermResponse(string Category, string Code, string ValueType, string Value,
    DateTime ValidFromUtc, DateTime? ValidUntilUtc, bool VisibleToCustomer, IReadOnlyDictionary<string, string> Parameters)
{
    public static CommercialTermResponse From(CommercialTerm term) => new(term.Category, term.Code, term.ValueType, term.Value,
        term.ValidFromUtc, term.ValidUntilUtc, term.VisibleToCustomer,
        JsonSerializer.Deserialize<Dictionary<string, string>>(term.ParametersJson) ?? []);
}
