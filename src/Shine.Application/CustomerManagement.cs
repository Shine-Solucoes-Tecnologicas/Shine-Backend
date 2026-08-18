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

public static class CustomerErrors
{
    public static readonly Error NotFound = new("customer_not_found", "The requested customer was not found.", "NotFound");
    public static readonly Error TaxIdentifierConflict = new("customer_tax_identifier_conflict", "An active customer already uses this tax identifier in the current unit.", "Conflict");
    public static Error Validation(string message) => new("customer_validation_error", message, "Validation");
}

public interface ICustomerManagement
{
    Task<Result<CustomerDetails>> CreateAsync(Guid tenantId, CreateCustomerCommand command, CancellationToken cancellationToken = default);
    Task<Result<CustomerDetails>> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
    Task<Result<CustomerDetails>> UpdateAsync(Guid tenantId, Guid customerId, UpdateCustomerCommand command, CancellationToken cancellationToken = default);
    Task<Result> DeactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
    Task<Result<CustomerDetails>> ReactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default);
}
