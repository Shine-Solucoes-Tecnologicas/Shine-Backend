using Microsoft.EntityFrameworkCore;
using Npgsql;
using Shine.Application;
using Shine.Domain;
using Shine.Infrastructure.Persistence;

namespace Shine.Infrastructure;

public sealed class CustomerManagement(ShineDbContext db) : ICustomerManagement
{
    private const string TaxIdentifierIndex = "IX_Customers_TenantId_TaxIdentifier";

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

    public async Task<Result<CustomerDetails>> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default)
    {
        var customer = await db.Customers.AsNoTracking()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == customerId, cancellationToken);
        return customer is null
            ? Result<CustomerDetails>.Failure(CustomerErrors.NotFound)
            : Result<CustomerDetails>.Success(ToDetails(customer));
    }

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
