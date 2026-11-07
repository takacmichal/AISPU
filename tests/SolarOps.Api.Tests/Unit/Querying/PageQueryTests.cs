using SolarOps.Api.Infrastructure.Querying;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.Querying;

public sealed class PageQueryTests
{
    [Fact]
    public void Defaults_AreValid_Page1_Size20()
    {
        var query = new PageQuery();

        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.Empty(DataAnnotationsValidator.Validate(query));
        Assert.Equal(0, query.Skip);
        Assert.Null(query.SearchTerm);
        Assert.Null(query.Descending);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-3, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void InvalidPaging_ReportsTheQueryParameterName(int page, int pageSize, string expectedKey)
    {
        var errors = DataAnnotationsValidator.Validate(new PageQuery(page, pageSize));

        Assert.Equal([expectedKey], errors.Keys);
    }

    [Theory]
    [InlineData("sideways")]
    [InlineData("up")]
    public void UnknownSortDir_ReportsSortDir(string sortDir)
    {
        var errors = DataAnnotationsValidator.Validate(new PageQuery(SortDir: sortDir));

        Assert.Contains("sortDir", errors.Keys);
    }

    [Theory]
    [InlineData("asc", false)]
    [InlineData("DESC", true)]
    public void SortDir_IsCaseInsensitive(string sortDir, bool descending)
    {
        var query = new PageQuery(SortDir: sortDir);

        Assert.Empty(DataAnnotationsValidator.Validate(query));
        Assert.Equal(descending, query.Descending);
    }

    [Fact]
    public void TooLongSearch_ReportsQ()
    {
        var errors = DataAnnotationsValidator.Validate(new PageQuery(Q: new string('x', 201)));

        Assert.Contains("q", errors.Keys);
    }

    [Fact]
    public void MultipleProblems_AreAllReported()
    {
        var errors = DataAnnotationsValidator.Validate(new PageQuery(0, 500, SortDir: "x"));

        Assert.Equal(["page", "pageSize", "sortDir"], errors.Keys.Order());
    }

    [Theory]
    [InlineData(1, 20, 0)]
    [InlineData(3, 20, 40)]
    [InlineData(int.MaxValue, 100, int.MaxValue)]
    public void Skip_IsComputedWithoutOverflow(int page, int pageSize, int expected) =>
        Assert.Equal(expected, new PageQuery(page, pageSize).Skip);

    [Fact]
    public void SearchTerm_IsTrimmed() => Assert.Equal("abc", new PageQuery(Q: "  abc ").SearchTerm);

    [Fact]
    public void BlankSearch_IsNull() => Assert.Null(new PageQuery(Q: "   ").SearchTerm);
}
