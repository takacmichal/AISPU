using System.Net;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Quotes;

/// <summary><c>GET /api/quotes/paged</c>: paging, filters, search, sorting and status counts.</summary>
public sealed class QuotePagedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Url = "/api/quotes/paged";
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 8, 0, 0, TimeSpan.Zero);

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    private sealed record Seed(CustomerResponse Novak, CustomerResponse Zeleny);

    /// <summary>
    /// Novak: Draft (net 100), Sent (200), Accepted (300). Zeleny: Draft (400), Rejected (500).
    /// Created one hour apart in that order.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        using var client = Sales();
        var novak = await CustomerTestData.CreateAsync(client, CustomerTestData.Person("n@example.sk", lastName: "Novák"));
        var zeleny = await CustomerTestData.CreateAsync(client, CustomerTestData.Person("z@example.sk", lastName: "Zelený", firstName: "Peter"));
        var by = Guid.CreateVersion7();

        Quote New(Guid customerId, decimal price, int hour) => Quote.Create(
            customerId,
            [new QuoteLineData(Guid.CreateVersion7(), ComponentType.Panel, "PAN-1", "Panel", price, 1m)],
            23m, null, by, T0.AddHours(hour));

        var draft1 = New(novak.Id, 100m, 0);
        var sent = New(novak.Id, 200m, 1);
        sent.Send(30, by, null, T0.AddHours(1));
        var accepted = New(novak.Id, 300m, 2);
        accepted.Send(30, by, null, T0.AddHours(2));
        accepted.Accept(by, null, T0.AddHours(2));
        var draft2 = New(zeleny.Id, 400m, 3);
        var rejected = New(zeleny.Id, 500m, 4);
        rejected.Send(30, by, null, T0.AddHours(4));
        rejected.Reject(by, null, T0.AddHours(4));

        await _factory.ExecuteDbAsync(async db =>
        {
            db.AddRange(draft1, sent, accepted, draft2, rejected);
            await db.SaveChangesAsync();
        });
        return new Seed(novak, zeleny);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Customer)]
    [InlineData(Roles.Technician)]
    public async Task RoleWithoutQuotesRead_Returns403_SoACustomerNeverSeesQuotes(string role)
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Empty_ReturnsDefaultsAndAllStatusesWithZero()
    {
        using var client = Sales();

        var page = await client.GetJsonAsync<QuotePageResponse>(Url);

        Assert.Empty(page.Items);
        Assert.Equal((1, 20, 0), (page.Page, page.PageSize, page.TotalCount));
        Assert.Equal(["Draft", "Sent", "Accepted", "Rejected", "Expired"], page.StatusCounts.Keys);
        Assert.All(page.StatusCounts.Values, c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task DefaultOrder_IsNewestFirst_WithLinesAndCounts()
    {
        await SeedAsync();
        using var client = Sales();

        var page = await client.GetJsonAsync<QuotePageResponse>(Url);

        Assert.Equal(5, page.TotalCount);
        Assert.Equal([500m, 400m, 300m, 200m, 100m], page.Items.Select(q => q.NetTotal));
        Assert.All(page.Items, q => Assert.Single(q.Lines));
        Assert.Equal(2, page.StatusCounts["Draft"]);
        Assert.Equal(1, page.StatusCounts["Sent"]);
        Assert.Equal(1, page.StatusCounts["Accepted"]);
        Assert.Equal(1, page.StatusCounts["Rejected"]);
        Assert.Equal(0, page.StatusCounts["Expired"]);
    }

    [Fact]
    public async Task StatusFilter_NarrowsItems_ButNotTheCounts()
    {
        await SeedAsync();
        using var client = Sales();

        var page = await client.GetJsonAsync<QuotePageResponse>($"{Url}?status=draft");

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, q => Assert.Equal(QuoteStatus.Draft, q.Status));
        Assert.Equal(5, page.StatusCounts.Values.Sum());
    }

    [Fact]
    public async Task CustomerIdFilter_AffectsItemsAndCounts()
    {
        var seed = await SeedAsync();
        using var client = Sales();

        var page = await client.GetJsonAsync<QuotePageResponse>($"{Url}?customerId={seed.Zeleny.Id}");

        Assert.Equal(2, page.TotalCount);
        Assert.All(page.Items, q => Assert.Equal(seed.Zeleny.Id, q.CustomerId));
        Assert.Equal(1, page.StatusCounts["Draft"]);
        Assert.Equal(1, page.StatusCounts["Rejected"]);
        Assert.Equal(0, page.StatusCounts["Accepted"]);
    }

    [Theory]
    [InlineData("zelen", 2)]
    [InlineData("PETER", 2)]
    [InlineData("novák", 3)]
    [InlineData("nobody", 0)]
    [InlineData("%", 0)]
    public async Task Search_MatchesCustomerNameCaseInsensitively_AndTreatsWildcardsLiterally(string q, int expected)
    {
        await SeedAsync();
        using var client = Sales();

        var page = await client.GetJsonAsync<QuotePageResponse>($"{Url}?q={Uri.EscapeDataString(q)}");

        Assert.Equal(expected, page.TotalCount);
        Assert.Equal(expected, page.StatusCounts.Values.Sum());
    }

    [Fact]
    public async Task Paging_ReturnsRequestedSlice_AndTotal()
    {
        await SeedAsync();
        using var client = Sales();

        var second = await client.GetJsonAsync<QuotePageResponse>($"{Url}?page=2&pageSize=2");
        var beyond = await client.GetJsonAsync<QuotePageResponse>($"{Url}?page=9&pageSize=2");

        Assert.Equal([300m, 200m], second.Items.Select(q => q.NetTotal));
        Assert.Equal((2, 2, 5), (second.Page, second.PageSize, second.TotalCount));
        Assert.Empty(beyond.Items);
        Assert.Equal(5, beyond.TotalCount);
    }

    [Theory]
    [InlineData("grossTotal", "asc", 100)]
    [InlineData("grossTotal", "desc", 500)]
    [InlineData("createdAt", "asc", 100)]
    public async Task Sort_ByWhitelistedColumn(string sortBy, string sortDir, decimal firstNet)
    {
        await SeedAsync();
        using var client = Sales();

        var page = await client.GetJsonAsync<QuotePageResponse>($"{Url}?sortBy={sortBy}&sortDir={sortDir}");

        Assert.Equal(firstNet, page.Items[0].NetTotal);
    }

    [Theory]
    [InlineData("sortBy=password", "sortBy")]
    [InlineData("sortDir=up", "sortDir")]
    [InlineData("status=Done", "status")]
    [InlineData("status=42", "status")]
    [InlineData("pageSize=101", "pageSize")]
    [InlineData("page=0", "page")]
    public async Task InvalidQuery_Returns400WithFieldName(string query, string field)
    {
        using var client = Sales();

        using var response = await client.GetAsync($"{Url}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Fact]
    public async Task ExistingArrayEndpoint_IsUnchanged()
    {
        await SeedAsync();
        using var client = Sales();

        var all = await client.GetJsonAsync<List<QuoteResponse>>("/api/quotes");

        Assert.Equal(5, all.Count);
    }
}
