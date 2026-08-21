using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shine.Application;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public sealed class CustomerManagement(ShineDbContext db) : ICustomerManagement, ICustomerReferenceValidator
{
    private const string TaxIdentifierIndex = "IX_Customers_TenantId_TaxIdentifier";
    private static readonly HashSet<string> AllowedSortFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "email", "phone", "createdAt", "updatedAt"
    };

    public async Task<Result<CustomerDetails>> CreateAsync(Guid tenantId, CreateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        Customer customer;
        try
        {
            customer = new Customer(tenantId, command.Name, command.Email, command.Phone, command.TaxIdentifier);
        }
        catch (ArgumentException exception)
        {
            return Result<CustomerDetails>.Failure(CustomerErrors.Validation(exception.Message));
        }

        db.Customers.Add(customer);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsTaxIdentifierConflict(exception))
        {
            return Result<CustomerDetails>.Failure(CustomerErrors.TaxIdentifierConflict);
        }

        return Result<CustomerDetails>.Success(ToDetails(customer));
    }

    public async Task<Result<PagedResponse<CustomerDetails>>> ListAsync(
        Guid tenantId,
        CustomerListQuery request,
        CancellationToken cancellationToken = default)
    {
        var sortBy = string.IsNullOrWhiteSpace(request.Page.SortBy) ? "name" : request.Page.SortBy.Trim();
        if (!AllowedSortFields.Contains(sortBy))
            return Result<PagedResponse<CustomerDetails>>.Failure(CustomerErrors.InvalidSort(sortBy));

        var query = request.IsActive == false
            ? db.Customers.IgnoreQueryFilters().Where(x => x.TenantId == tenantId && x.IsDeleted)
            : db.Customers.Where(x => x.TenantId == tenantId);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var search = request.Search.Trim();
            var normalizedName = search.ToUpperInvariant();
            var normalizedEmail = search.ToLowerInvariant();
            var normalizedPhone = new string(search.Where(char.IsDigit).ToArray());

            query = query.Where(x =>
                x.NormalizedName.StartsWith(normalizedName) ||
                x.Email != null && x.Email.StartsWith(normalizedEmail) ||
                normalizedPhone.Length > 0 && x.Phone != null && x.Phone.StartsWith(normalizedPhone));
        }

        query = ApplyOrdering(query, sortBy, request.Page.Descending);

        var page = request.Page.ValidatedPage;
        var pageSize = request.Page.ValidatedPageSize;
        var total = await query.CountAsync(cancellationToken);
        var customers = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToArrayAsync(cancellationToken);
        var response = PagedResponse<CustomerDetails>.Create(customers.Select(ToDetails).ToArray(), page, pageSize, total);
        return Result<PagedResponse<CustomerDetails>>.Success(response);
    }

    public async Task<Result<CustomerDetails>> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == customerId, cancellationToken);
        return customer is null
            ? Result<CustomerDetails>.Failure(CustomerErrors.NotFound)
            : Result<CustomerDetails>.Success(ToDetails(customer));
    }

    public Task<bool> IsActiveInTenantAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default) =>
        customerId != Guid.Empty
            ? db.Customers.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.Id == customerId, cancellationToken)
            : Task.FromResult(false);

    public async Task<Result<CustomerDetails>> UpdateAsync(Guid tenantId, Guid customerId, UpdateCustomerCommand command, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == customerId, cancellationToken);
        if (customer is null) return Result<CustomerDetails>.Failure(CustomerErrors.NotFound);

        try
        {
            customer.Update(command.Name, command.Email, command.Phone, command.TaxIdentifier);
        }
        catch (ArgumentException exception)
        {
            return Result<CustomerDetails>.Failure(CustomerErrors.Validation(exception.Message));
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsTaxIdentifierConflict(exception))
        {
            return Result<CustomerDetails>.Failure(CustomerErrors.TaxIdentifierConflict);
        }

        return Result<CustomerDetails>.Success(ToDetails(customer));
    }

    public async Task<Result> DeactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == customerId, cancellationToken);
        if (customer is null) return Result.Failure(CustomerErrors.NotFound);

        customer.Deactivate(DateTime.UtcNow);
        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public async Task<Result<CustomerDetails>> ReactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == customerId, cancellationToken);
        if (customer is null) return Result<CustomerDetails>.Failure(CustomerErrors.NotFound);
        if (customer.IsActive) return Result<CustomerDetails>.Success(ToDetails(customer));

        customer.Reactivate();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsTaxIdentifierConflict(exception))
        {
            return Result<CustomerDetails>.Failure(CustomerErrors.TaxIdentifierConflict);
        }

        return Result<CustomerDetails>.Success(ToDetails(customer));
    }

    private static bool IsTaxIdentifierConflict(DbUpdateException exception) =>
        exception.InnerException is PostgresException postgres &&
        postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
        postgres.ConstraintName == TaxIdentifierIndex;

    private static IQueryable<Customer> ApplyOrdering(IQueryable<Customer> query, string sortBy, bool descending) =>
        (sortBy.ToLowerInvariant(), descending) switch
        {
            ("name", false) => query.OrderBy(x => x.NormalizedName).ThenBy(x => x.Id),
            ("name", true) => query.OrderByDescending(x => x.NormalizedName).ThenBy(x => x.Id),
            ("email", false) => query.OrderBy(x => x.Email).ThenBy(x => x.Id),
            ("email", true) => query.OrderByDescending(x => x.Email).ThenBy(x => x.Id),
            ("phone", false) => query.OrderBy(x => x.Phone).ThenBy(x => x.Id),
            ("phone", true) => query.OrderByDescending(x => x.Phone).ThenBy(x => x.Id),
            ("createdat", false) => query.OrderBy(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            ("createdat", true) => query.OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.Id),
            ("updatedat", false) => query.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            ("updatedat", true) => query.OrderByDescending(x => x.UpdatedAtUtc).ThenBy(x => x.Id),
            _ => throw new InvalidOperationException("Customer sort field was not validated.")
        };

    private static CustomerDetails ToDetails(Customer customer) => new(
        customer.Id,
        customer.TenantId,
        customer.Name,
        customer.Email,
        customer.Phone,
        customer.TaxIdentifier,
        customer.IsActive,
        customer.CreatedAtUtc,
        customer.UpdatedAtUtc);
}
