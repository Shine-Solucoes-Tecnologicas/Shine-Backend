using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shine.Application;
using Shine.Infrastructure;

namespace Shine.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/customers")]
public sealed class CustomersController(
    ICustomerManagement customers,
    ICurrentTenant currentTenant,
    ICustomerHistoryReader? customerHistory = null) : ControllerBase
{
    [HttpGet]
    [RequiresPermission("customers.read")]
    [ProducesResponseType<PagedResponse<CustomerDetails>>(StatusCodes.Status200OK)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResponse<CustomerDetails>>> List(
        [FromQuery] CustomerListRequest request,
        CancellationToken cancellationToken)
    {
        var result = await customers.ListAsync(RequireTenant(), request.ToQuery(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpPost]
    [RequiresPermission("customers.manage")]
    [ProducesResponseType<CustomerDetails>(StatusCodes.Status201Created)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDetails>> Create(CustomerWriteRequest request, CancellationToken cancellationToken)
    {
        var result = await customers.CreateAsync(RequireTenant(), request.ToCreateCommand(), cancellationToken);
        if (result.IsFailure) return Error(result);
        return CreatedAtAction(nameof(Get), new { customerId = result.Value!.Id }, result.Value);
    }

    [HttpGet("{customerId:guid}")]
    [RequiresPermission("customers.read")]
    [ProducesResponseType<CustomerDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerDetails>> Get(Guid customerId, CancellationToken cancellationToken)
    {
        var result = await customers.GetAsync(RequireTenant(), customerId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpGet("{customerId:guid}/history")]
    [RequiresPermission("customers.read")]
    [RequiresPermission("scheduling.read")]
    [RequiresModule("SCHEDULING")]
    [ProducesResponseType<CustomerHistoryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerHistoryResponse>> History(
        Guid customerId,
        [FromQuery] CustomerHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = RequireTenant();
        var customer = await customers.GetAsync(tenantId, customerId, cancellationToken);
        if (customer.IsFailure) return Error(customer);
        if (customerHistory is null)
            throw new InvalidOperationException("Customer history is not configured.");

        var page = await customerHistory.ListAsync(tenantId, customerId, request.ToQuery(), cancellationToken);
        return Ok(new CustomerHistoryResponse(
            "1.0",
            page.Items,
            page.Page,
            page.PageSize,
            page.TotalItems,
            page.TotalPages));
    }

    [HttpPut("{customerId:guid}")]
    [RequiresPermission("customers.manage")]
    [ProducesResponseType<CustomerDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDetails>> Update(Guid customerId, CustomerWriteRequest request, CancellationToken cancellationToken)
    {
        var result = await customers.UpdateAsync(RequireTenant(), customerId, request.ToUpdateCommand(), cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    [HttpDelete("{customerId:guid}")]
    [RequiresPermission("customers.manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid customerId, CancellationToken cancellationToken)
    {
        var result = await customers.DeactivateAsync(RequireTenant(), customerId, cancellationToken);
        return result.IsSuccess ? NoContent() : Error(result);
    }

    [HttpPost("{customerId:guid}/reactivate")]
    [RequiresPermission("customers.manage")]
    [ProducesResponseType<CustomerDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<CustomerErrorResponse>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CustomerDetails>> Reactivate(Guid customerId, CancellationToken cancellationToken)
    {
        var result = await customers.ReactivateAsync(RequireTenant(), customerId, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : Error(result);
    }

    private Guid RequireTenant() => currentTenant.TenantId is Guid tenantId && currentTenant.HasCompleteContext
        ? tenantId
        : throw new UnauthorizedAccessException("An active tenant context is required.");

    private ObjectResult Error(Result result)
    {
        var statusCode = result.Errors.Any(error => error.Category == "Conflict")
            ? StatusCodes.Status409Conflict
            : result.Errors.Any(error => error.Category == "NotFound")
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest;
        return StatusCode(statusCode, new CustomerErrorResponse(
            result.Errors.Select(error => new CustomerError(error.Code, error.Message)).ToArray()));
    }
}

public sealed record CustomerListRequest(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20,
    string? SortBy = null,
    bool Descending = false)
{
    public CustomerListQuery ToQuery() => new(Search, IsActive, new PagedRequest(Page, PageSize, SortBy, Descending));
}

public sealed record CustomerHistoryRequest(int Page = 1, int PageSize = 20)
{
    public CustomerHistoryQuery ToQuery() => new(new PagedRequest(Page, PageSize));
}

public sealed class CustomerWriteRequest
{
    public CustomerWriteRequest()
    {
    }

    public CustomerWriteRequest(string name, string? email, string? phone, string? taxIdentifier)
    {
        Name = name;
        Email = email;
        Phone = phone;
        TaxIdentifier = taxIdentifier;
    }

    [Required, MaxLength(200)]
    public string Name { get; init; } = string.Empty;

    [EmailAddress, MaxLength(320)]
    public string? Email { get; init; }

    [MaxLength(40)]
    public string? Phone { get; init; }

    [MaxLength(30)]
    public string? TaxIdentifier { get; init; }

    public CreateCustomerCommand ToCreateCommand() => new(Name, Email, Phone, TaxIdentifier);
    public UpdateCustomerCommand ToUpdateCommand() => new(Name, Email, Phone, TaxIdentifier);
}

public sealed record CustomerError(string Code, string Message);
public sealed record CustomerErrorResponse(IReadOnlyCollection<CustomerError> Errors);
