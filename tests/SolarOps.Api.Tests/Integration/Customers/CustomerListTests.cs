using System.Net;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Features.Tickets;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Tickets;

namespace SolarOps.Api.Tests.Integration.Customers;

/// <summary>Filters, status segment counts and list summaries of <c>GET /api/customers</c>.</summary>
public sealed class CustomerListTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Url = "/api/customers";

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    /// <summary>2 active persons (Adamec, Zelený), 1 active company (Bratia), 1 archived person (Mráz).</summary>
    private async Task SeedAsync(HttpClient client)
    {
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("a@example.sk", lastName: "Adamec"));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person("z@example.sk", lastName: "Zelený"));
        await CustomerTestData.CreateAsync(client, CustomerTestData.Company("b@example.sk", companyName: "Bratia s.r.o."));
        var archived = await CustomerTestData.CreateAsync(client, CustomerTestData.Person("m@example.sk", lastName: "Mráz"));
        using var response = await client.PostAsync($"{Url}/{archived.Id}/archive", null);
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CustomerRole_Returns403_SoNoCustomerSeesAnyCustomerData()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task StatusCounts_ListEveryStatus_EvenWhenEmpty()
    {
        using var client = Sales();

        var list = await client.GetJsonAsync<CustomerListResponse>(Url);

        Assert.Equal(["Active", "Archived"], list.StatusCounts.Keys);
        Assert.All(list.StatusCounts.Values, count => Assert.Equal(0, count));
    }

    [Fact]
    public async Task StatusCounts_IgnoreStatusFilter_ButHonourSearchAndType()
    {
        using var client = Sales();
        await SeedAsync(client);

        var all = await client.GetJsonAsync<CustomerListResponse>(Url);
        var archivedOnly = await client.GetJsonAsync<CustomerListResponse>($"{Url}?status=Archived");
        var persons = await client.GetJsonAsync<CustomerListResponse>($"{Url}?type=Person");
        var searched = await client.GetJsonAsync<CustomerListResponse>($"{Url}?q=adam");

        Assert.Equal(3, all.StatusCounts["Active"]);
        Assert.Equal(1, all.StatusCounts["Archived"]);
        Assert.Equal(4, all.TotalCount);

        Assert.Equal(["m@example.sk"], archivedOnly.Items.Select(c => c.Email));
        Assert.Equal(1, archivedOnly.TotalCount);
        Assert.Equal(3, archivedOnly.StatusCounts["Active"]);
        Assert.Equal(1, archivedOnly.StatusCounts["Archived"]);

        Assert.Equal(2, persons.StatusCounts["Active"]);
        Assert.Equal(1, persons.StatusCounts["Archived"]);

        Assert.Equal(1, searched.StatusCounts["Active"]);
        Assert.Equal(0, searched.StatusCounts["Archived"]);
    }

    [Fact]
    public async Task TypeAndStatusFilters_Combine()
    {
        using var client = Sales();
        await SeedAsync(client);

        var companies = await client.GetJsonAsync<CustomerListResponse>($"{Url}?type=Company");
        var activePersons = await client.GetJsonAsync<CustomerListResponse>($"{Url}?type=Person&status=Active");

        Assert.Equal(["b@example.sk"], companies.Items.Select(c => c.Email));
        Assert.Equal(["a@example.sk", "z@example.sk"], activePersons.Items.Select(c => c.Email));
        Assert.Equal(2, activePersons.TotalCount);
    }

    [Theory]
    [InlineData("type=99", "type")]
    [InlineData("status=Gone", "status")]
    [InlineData("pageSize=500", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("sortBy=nope", "sortBy")]
    [InlineData("sortDir=sideways", "sortDir")]
    public async Task InvalidQuery_Returns400WithFieldName(string query, string field)
    {
        using var client = Sales();

        using var response = await client.GetAsync($"{Url}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Fact]
    public async Task Items_CarrySupplyPointCount_AndLastActivityFromOwnRecordsOnly()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        var ticket = await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id));
        var future = DateTimeOffset.UtcNow.AddDays(30);
        var by = Guid.CreateVersion7();

        await _factory.ExecuteDbAsync(async db =>
        {
            // Owned types must not be shared between owners, hence one address instance per supply point.
            static PostalAddress Address() => new("Hlavná 1", "Nitra", "949 01");
            db.AddRange(
                SupplyPoint.Create(seed.CustomerA.Id, "Dom", $"24Z{Guid.NewGuid():N}"[..16], Address(), 25, future),
                SupplyPoint.Create(seed.CustomerA.Id, "Garáž", $"24Z{Guid.NewGuid():N}"[..16], Address(), 16, future));
            // A quote of customer B updated in the future must not leak into customer A.
            db.Add(Quote.Create(
                seed.CustomerB.Id,
                [new QuoteLineData(Guid.CreateVersion7(), ComponentType.Panel, "PAN-1", "Panel", 100m, 1m)],
                23m, null, by, future));
            await db.SaveChangesAsync();
        });

        using var client = Sales();
        var list = await client.GetJsonAsync<CustomerListResponse>(Url);
        var a = list.Items.Single(c => c.Id == seed.CustomerA.Id);
        var b = list.Items.Single(c => c.Id == seed.CustomerB.Id);

        Assert.Equal(2, a.SupplyPointCount);
        Assert.Equal(0, b.SupplyPointCount);

        var expectedA = new[] { a.UpdatedAt, seed.InstallationA.UpdatedAt, ticket.UpdatedAt }.Max();
        Assert.Equal(expectedA, a.LastActivityAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(future, b.LastActivityAt, TimeSpan.FromMilliseconds(1));
    }

    [Fact]
    public async Task LastActivity_WithoutRelatedRecords_IsTheCustomerUpdate()
    {
        using var client = Sales();
        var customer = await CustomerTestData.CreateAsync(client, CustomerTestData.Person());

        var list = await client.GetJsonAsync<CustomerListResponse>(Url);

        var item = Assert.Single(list.Items);
        Assert.Equal(customer.UpdatedAt, item.LastActivityAt, TimeSpan.FromMilliseconds(1));
        Assert.Equal(0, item.SupplyPointCount);
    }
}
