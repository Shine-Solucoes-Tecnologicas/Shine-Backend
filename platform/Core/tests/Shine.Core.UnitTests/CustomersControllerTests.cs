using Microsoft.AspNetCore.Mvc;
using Shine.Api.Controllers;
using Shine.Application;
using Shine.Infrastructure;

namespace Shine.UnitTests;

public sealed class CustomersControllerTests
{
    [Fact]
    public async Task List_uses_active_tenant_and_maps_query_contract()
    {
        var tenantId = Guid.NewGuid();
        var service = new TestCustomerManagement
        {
            ListResult = Result<PagedResponse<CustomerDetails>>.Success(
                PagedResponse<CustomerDetails>.Create([], 2, 50, 0))
        };
        var controller = new CustomersController(service, new TestTenant(tenantId));

        var action = await controller.List(new CustomerListRequest("maria", false, 2, 50, "email", true), default);

        var response = Assert.IsType<OkObjectResult>(action.Result);
        var page = Assert.IsType<PagedResponse<CustomerDetails>>(response.Value);
        Assert.Equal(2, page.Page);
        Assert.Equal(tenantId, service.ReceivedTenantId);
        Assert.Equal(new CustomerListQuery("maria", false, new PagedRequest(2, 50, "email", true)), service.ReceivedListQuery);
    }

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

    [Fact]
    public async Task History_returns_versioned_page_without_moving_source_data_into_customer_management()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var item = new CustomerHistoryItem(
            "scheduling.appointment", "scheduling", Guid.NewGuid(), DateTime.UtcNow,
            new AppointmentCustomerHistoryDetails(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(30), "Scheduled"));
        var customers = new TestCustomerManagement
        {
            GetResult = Result<CustomerDetails>.Success(Details(customerId, tenantId))
        };
        var history = new TestCustomerHistoryReader(item);
        var controller = new CustomersController(customers, new TestTenant(tenantId), history);

        var action = await controller.History(customerId, new CustomerHistoryRequest(2, 10), default);

        var response = Assert.IsType<CustomerHistoryResponse>(Assert.IsType<OkObjectResult>(action.Result).Value);
        Assert.Equal("1.0", response.SchemaVersion);
        Assert.Equal(item, Assert.Single(response.Items));
        Assert.Equal(tenantId, history.TenantId);
        Assert.Equal(customerId, history.CustomerId);
        Assert.Equal(2, history.Query!.Page.Page);
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
        public Result<PagedResponse<CustomerDetails>> ListResult { get; init; } =
            Result<PagedResponse<CustomerDetails>>.Failure(CustomerErrors.NotFound);
        public CustomerListQuery? ReceivedListQuery { get; private set; }

        public Task<Result<CustomerDetails>> CreateAsync(Guid tenantId, CreateCustomerCommand command, CancellationToken cancellationToken = default)
        {
            ReceivedTenantId = tenantId;
            return Task.FromResult(CreateResult);
        }

        public Task<Result<PagedResponse<CustomerDetails>>> ListAsync(Guid tenantId, CustomerListQuery query, CancellationToken cancellationToken = default)
        {
            ReceivedTenantId = tenantId;
            ReceivedListQuery = query;
            return Task.FromResult(ListResult);
        }

        public Task<Result<CustomerDetails>> GetAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(GetResult);
        public Task<Result<CustomerDetails>> UpdateAsync(Guid tenantId, Guid customerId, UpdateCustomerCommand command, CancellationToken cancellationToken = default) => Task.FromResult(GetResult);
        public Task<Result> DeactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
        public Task<Result<CustomerDetails>> ReactivateAsync(Guid tenantId, Guid customerId, CancellationToken cancellationToken = default) => Task.FromResult(GetResult);
    }

    private sealed class TestCustomerHistoryReader(CustomerHistoryItem item) : ICustomerHistoryReader
    {
        public Guid? TenantId { get; private set; }
        public Guid? CustomerId { get; private set; }
        public CustomerHistoryQuery? Query { get; private set; }

        public Task<PagedResponse<CustomerHistoryItem>> ListAsync(Guid tenantId, Guid customerId,
            CustomerHistoryQuery query, CancellationToken cancellationToken = default)
        {
            TenantId = tenantId;
            CustomerId = customerId;
            Query = query;
            return Task.FromResult(PagedResponse<CustomerHistoryItem>.Create([item], query.Page.Page, query.Page.PageSize, 1));
        }
    }
}
