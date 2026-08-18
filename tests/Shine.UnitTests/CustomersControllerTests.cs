using Microsoft.AspNetCore.Mvc;
using Shine.Api.Controllers;
using Shine.Application;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class CustomersControllerTests
{
    [Fact]
    public async Task Create_uses_active_tenant_and_returns_created_contract()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var service = new TestCustomerManagement
        {
            CreateResult = Result<CustomerDetails>.Success(Details(customerId, tenantId))
        };
        var controller = new CustomersController(service, new TestTenant(tenantId));

        var action = await controller.Create(new CustomerWriteRequest("Customer", null, null, null), default);

        var created = Assert.IsType<CreatedAtActionResult>(action.Result);
        Assert.Equal(nameof(CustomersController.Get), created.ActionName);
        Assert.Equal(customerId, Assert.IsType<CustomerDetails>(created.Value).Id);
        Assert.Equal(tenantId, service.ReceivedTenantId);
    }

    [Theory]
    [InlineData("Validation", 400)]
    [InlineData("NotFound", 404)]
    [InlineData("Conflict", 409)]
    public async Task Get_maps_application_error_categories_to_http(string category, int expectedStatus)
    {
        var service = new TestCustomerManagement
        {
            GetResult = Result<CustomerDetails>.Failure(new Error("customer_error", "Safe error.", category))
        };
        var controller = new CustomersController(service, new TestTenant(Guid.NewGuid()));

        var action = await controller.Get(Guid.NewGuid(), default);

        var error = Assert.IsType<ObjectResult>(action.Result);
        Assert.Equal(expectedStatus, error.StatusCode);
        Assert.Equal("customer_error", Assert.Single(Assert.IsType<CustomerErrorResponse>(error.Value).Errors).Code);
    }

    private static CustomerDetails Details(Guid id, Guid tenantId) =>
        new(id, tenantId, "Customer", null, null, null, true, DateTime.UtcNow, null);

    private sealed class TestTenant(Guid tenantId) : ICurrentTenant
    {
        public Guid? TenantId => tenantId;
        public Guid? UserTenantId => Guid.NewGuid();
        public IReadOnlyCollection<string> Roles => [];
        public bool HasCompleteContext => true;
    }

    private sealed class TestCustomerManagement : ICustomerManagement
    {
        public Guid? ReceivedTenantId { get; private set; }
        public Result<CustomerDetails> CreateResult { get; init; } = Result<CustomerDetails>.Failure(CustomerErrors.NotFound);
        public Result<CustomerDetails> GetResult { get; init; } = Result<CustomerDetails>.Failure(CustomerErrors.NotFound);

        public Task<Result<CustomerDetails>> CreateAsync(Guid tenantId, CreateCustomerCommand command, CancellationToken cancellationToken = default)
        {
            ReceivedTenantId = tenantId;
            return Task.FromResult(CreateResult);
        }

        public Task<Result<CustomerDetails>> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(GetResult);
        public Task<Result<CustomerDetails>> UpdateAsync(Guid tenantId, Guid customerId, UpdateCustomerCommand command, CancellationToken cancellationToken = default) => Task.FromResult(GetResult);
        public Task<Result> DeactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<CustomerDetails>> ReactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(GetResult);
    }
}
