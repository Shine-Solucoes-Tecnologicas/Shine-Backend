using System.Linq.Expressions;

namespace Shine.Application;

public sealed record PagedRequest(int Page = 1, int PageSize = 20, string? SortBy = null, bool Descending = false)
{
    public int ValidatedPage => Math.Max(1, Page);
    public int ValidatedPageSize => Math.Clamp(PageSize, 1, 100);
}

public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalItems, int TotalPages)
{
    public static PagedResponse<T> Create(IReadOnlyCollection<T> items, int page, int pageSize, int totalItems) =>
        new(items, page, pageSize, totalItems, (int)Math.Ceiling(totalItems / (double)pageSize));
}

public static class PaginationExtensions
{
    public static IQueryable<T> ApplyOrdering<T>(this IQueryable<T> query, string? sortBy, bool descending, IReadOnlyDictionary<string, Expression<Func<T, object>>> allowed)
    {
        if (string.IsNullOrWhiteSpace(sortBy) || !allowed.TryGetValue(sortBy, out var expression)) return query;
        return descending ? query.OrderByDescending(expression) : query.OrderBy(expression);
    }

    public static IQueryable<T> ApplyPaging<T>(this IQueryable<T> query, PagedRequest request)
    {
        var page = request.ValidatedPage;
        var pageSize = request.ValidatedPageSize;
        return query.Skip((page - 1) * pageSize).Take(pageSize);
    }
}
