namespace Shine.Application;

public sealed record CustomerDetails(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Email,
    string? Phone,
    string? TaxIdentifier,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record CreateCustomerCommand(string Name, string? Email, string? Phone, string? TaxIdentifier);

public sealed record UpdateCustomerCommand(string Name, string? Email, string? Phone, string? TaxIdentifier);

public sealed record CustomerListQuery(
    string? Search,
    bool? IsActive,
    PagedRequest Page);

public static class CustomerErrors
{
    public static readonly Error NotFound = new("customer_not_found", "The requested customer was not found.", "NotFound");
    public static readonly Error TaxIdentifierConflict = new("customer_tax_identifier_conflict", "An active customer already uses this tax identifier in the current unit.", "Conflict");
    public static Error InvalidSort(string sortBy) => new(
        "customer_sort_invalid",
        $"The sort field '{sortBy}' is not supported.",
        "Validation");
    public static Error Validation(string message) => new("customer_validation_error", message, "Validation");
}

public interface ICustomerManagement
{
    Task<Result<CustomerDetails>> CreateAsync(Guid tenantId, CreateCustomerCommand command, CancellationToken cancellationToken = default);
    Task<Result<PagedResponse<CustomerDetails>>> ListAsync(Guid tenantId, CustomerListQuery query, CancellationToken cancellationToken = default);
    Task<Result<CustomerDetails>> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
    Task<Result<CustomerDetails>> UpdateAsync(Guid tenantId, Guid customerId, UpdateCustomerCommand command, CancellationToken cancellationToken = default);
    Task<Result> DeactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
    Task<Result<CustomerDetails>> ReactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
}

public interface ICustomerReferenceValidator
{
    Task<bool> IsActiveInTenantAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
}

public sealed record CustomerHistoryQuery(PagedRequest Page);

/// <summary>
/// Stable envelope for operational history. New sources add a new item type and payload without changing
/// the envelope or copying the source data into the customer domain.
/// </summary>
public sealed record CustomerHistoryResponse(
    string SchemaVersion,
    IReadOnlyCollection<CustomerHistoryItem> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record CustomerHistoryItem(
    string Type,
    string Source,
    Guid SourceId,
    DateTime OccurredAtUtc,
    object Details);

public sealed record AppointmentCustomerHistoryDetails(
    Guid ProfessionalId,
    Guid ServiceId,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string Status);

public interface ICustomerHistoryReader
{
    Task<PagedResponse<CustomerHistoryItem>> ListAsync(
        Guid tenantId,
        Guid customerId,
        CustomerHistoryQuery query,
        CancellationToken cancellationToken = default);
}
