using System.Linq.Expressions;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Tests.Unit.Querying;

public sealed class QueryableExtensionsTests
{
    private enum Level
    {
        Low,
        Mid,
        High,
    }

    private sealed record Row(int Id, string Name, DateTimeOffset At, Level Level);

    private static readonly DateTimeOffset T0 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly Dictionary<string, Expression<Func<Row, object>>> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = r => r.Name,
        ["at"] = r => r.At,
    };

    private static readonly Row[] Rows =
    [
        new(1, "b", T0.AddDays(2), Level.Low),
        new(2, "a", T0.AddDays(3), Level.Low),
        new(3, "c", T0.AddDays(1), Level.High),
        new(4, "a", T0.AddDays(4), Level.Low),
    ];

    private static int[] Sorted(PageQuery query, string defaultSort = "name", bool defaultDescending = false) =>
        [.. Rows.AsQueryable().ApplySort(query, Map, r => r.Id, defaultSort, defaultDescending).Select(r => r.Id)];

    [Fact]
    public void ApplySort_UsesDefaultKeyAndTieBreaksByKey() => Assert.Equal([2, 4, 1, 3], Sorted(new PageQuery()));

    [Fact]
    public void ApplySort_RequestedKeyIsCaseInsensitive_AndValueTypesWork() =>
        Assert.Equal([3, 1, 2, 4], Sorted(new PageQuery(SortBy: "AT")));

    [Fact]
    public void ApplySort_Descending_ReversesTheSortButKeepsStableTieBreak() =>
        Assert.Equal([3, 1, 2, 4], Sorted(new PageQuery(SortBy: "name", SortDir: "desc")));

    [Fact]
    public void ApplySort_EndpointDefaultDirectionApplies_UnlessCallerChoosesOne()
    {
        Assert.Equal([4, 2, 1, 3], Sorted(new PageQuery(), "at", defaultDescending: true));
        Assert.Equal([3, 1, 2, 4], Sorted(new PageQuery(SortDir: "asc"), "at", defaultDescending: true));
    }

    [Fact]
    public void ApplySort_UnknownKey_ThrowsValidationErrorOnSortBy()
    {
        var ex = Assert.Throws<RequestValidationException>(() => Sorted(new PageQuery(SortBy: "password")));

        var message = Assert.Single(ex.Errors["sortBy"]);
        Assert.Contains("name", message);
        Assert.Contains("at", message);
    }

    [Fact]
    public void CompleteCounts_ContainsEveryEnumValue_WithZeroForMissing()
    {
        var counts = QueryableExtensions.CompleteCounts<Level>([new(Level.High, 5)]);

        Assert.Equal(["Low", "Mid", "High"], counts.Keys);
        Assert.Equal(0, counts["Low"]);
        Assert.Equal(0, counts["Mid"]);
        Assert.Equal(5, counts["High"]);
    }

    [Fact]
    public void CompleteCounts_NoRows_AllZero() =>
        Assert.All(QueryableExtensions.CompleteCounts<Level>([]), c => Assert.Equal(0, c.Value));

    [Fact]
    public void PagedResponse_Map_KeepsPagingInfo()
    {
        var mapped = new PagedResponse<int>([1, 2], 3, 2, 9).Map(i => i * 10);

        Assert.Equal([10, 20], mapped.Items);
        Assert.Equal((3, 2, 9), (mapped.Page, mapped.PageSize, mapped.TotalCount));
    }
}
