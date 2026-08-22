using System.ComponentModel.DataAnnotations;
using Shine.Application;

namespace Shine.UnitTests;

public sealed class PendingInfrastructureTests
{
    [Fact]
    public void Pagination_clamps_invalid_page_and_size()
    {
        var request = new PagedRequest(0, 1000);
        Assert.Equal(1, request.ValidatedPage);
        Assert.Equal(100, request.ValidatedPageSize);
    }

    [Fact]
    public void Pagination_orders_and_limits_items()
    {
        var values = new[] { new Row(3), new Row(1), new Row(2) }.AsQueryable();
        var allowed = new Dictionary<string, System.Linq.Expressions.Expression<Func<Row, object>>>
        { ["value"] = row => row.Value };

        var result = values.ApplyOrdering("value", descending: false, allowed).ApplyPaging(new PagedRequest(2, 1)).ToArray();

        Assert.Single(result);
        Assert.Equal(2, result[0].Value);
    }

    [Fact]
    public void Paged_response_calculates_total_pages()
    {
        var response = PagedResponse<int>.Create([1, 2], 1, 2, 5);
        Assert.Equal(3, response.TotalPages);
    }

    [Fact]
    public void Result_represents_success_and_failure()
    {
        var success = Result<int>.Success(42);
        var failure = Result<int>.Failure(ErrorCatalog.Validation);

        Assert.True(success.IsSuccess);
        Assert.Equal(42, success.Value);
        Assert.True(failure.IsFailure);
        Assert.Contains(ErrorCatalog.Validation, failure.Errors);
    }

    [Fact]
    public void Data_annotations_validation_returns_validation_errors()
    {
        var service = new DataAnnotationsValidationService();
        var errors = service.Validate(new RequiredModel());
        Assert.Contains(errors, error => error.Code == "validation_error");
    }

    private sealed record Row(int Value);
    private sealed class RequiredModel
    {
        [Required]
        public string? Name { get; init; }
    }
}
