using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Infrastructure.Querying;

/// <summary>Sorting, paging and per-status counts for list endpoints (see <see cref="PageQuery"/>).</summary>
public static class QueryableExtensions
{
    /// <summary>
    /// Orders by one of the whitelisted keys of <paramref name="sortMap"/> (matched case-insensitively) and always
    /// finishes with <paramref name="tieBreaker"/> so paging is stable. An unknown key is a 400 on <c>sortBy</c>.
    /// The key selectors may return value types (boxed to object); the boxing is stripped before EF translates them.
    /// </summary>
    public static IQueryable<T> ApplySort<T>(
        this IQueryable<T> source,
        PageQuery page,
        IReadOnlyDictionary<string, Expression<Func<T, object>>> sortMap,
        Expression<Func<T, object>> tieBreaker,
        string defaultSortBy,
        bool defaultDescending = false)
    {
        var requested = string.IsNullOrWhiteSpace(page.SortBy) ? defaultSortBy : page.SortBy.Trim();
        var entry = sortMap.FirstOrDefault(e => string.Equals(e.Key, requested, StringComparison.OrdinalIgnoreCase));
        if (entry.Value is null)
        {
            throw new RequestValidationException("sortBy", $"SortBy must be one of: {string.Join(", ", sortMap.Keys)}.");
        }

        var descending = page.Descending ?? defaultDescending;
        var ordered = OrderBy(source, entry.Value, descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy));
        return OrderBy(ordered, tieBreaker, nameof(Queryable.ThenBy));
    }

    /// <summary>Counts all matching rows, then loads the requested page. The query must already be ordered.</summary>
    public static async Task<PagedResponse<T>> ToPagedAsync<T>(this IQueryable<T> source, PageQuery page, CancellationToken ct = default)
    {
        var total = await source.CountAsync(ct);
        var items = total == 0 || page.Skip >= total
            ? []
            : await source.Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResponse<T>(items, page.Page, page.PageSize, total);
    }

    /// <summary>
    /// Counts rows per enum value in one grouped query. Every value of <typeparamref name="TKey"/> is present
    /// (0 when there is no row), keyed by its name in declaration order, so a UI can render all status segments.
    /// </summary>
    public static async Task<Dictionary<string, int>> CountByAsync<T, TKey>(
        this IQueryable<T> source, Expression<Func<T, TKey>> key, CancellationToken ct = default)
        where TKey : struct, Enum
    {
        var rows = await source.GroupBy(key).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        return CompleteCounts(rows.Select(r => new KeyValuePair<TKey, int>(r.Key, r.Count)));
    }

    /// <summary>Builds the name-to-count map with all enum values present (0 when missing).</summary>
    public static Dictionary<string, int> CompleteCounts<TKey>(IEnumerable<KeyValuePair<TKey, int>> counts)
        where TKey : struct, Enum
    {
        var byValue = counts.GroupBy(c => c.Key).ToDictionary(g => g.Key, g => g.Sum(c => c.Value));
        var result = new Dictionary<string, int>();
        foreach (var value in Enum.GetValues<TKey>())
        {
            result[value.ToString()] = byValue.GetValueOrDefault(value);
        }

        return result;
    }

    private static IQueryable<T> OrderBy<T>(IQueryable<T> source, Expression<Func<T, object>> key, string method)
    {
        var body = key.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert
            && convert.Type == typeof(object)
                ? convert.Operand
                : key.Body;
        var lambda = Expression.Lambda(body, key.Parameters);
        var call = Expression.Call(
            typeof(Queryable),
            method,
            [typeof(T), body.Type],
            source.Expression,
            Expression.Quote(lambda));
        return source.Provider.CreateQuery<T>(call);
    }
}
