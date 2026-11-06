namespace SolarOps.Api.Infrastructure.Querying;

/// <summary>One page of a list endpoint.</summary>
/// <param name="Items">Items of the requested page.</param>
/// <param name="Page">1-based page number.</param>
/// <param name="PageSize">Requested page size.</param>
/// <param name="TotalCount">Number of items matching the filters over all pages.</param>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public PagedResponse<TOut> Map<TOut>(Func<T, TOut> map) => new(Items.Select(map).ToList(), Page, PageSize, TotalCount);
}
