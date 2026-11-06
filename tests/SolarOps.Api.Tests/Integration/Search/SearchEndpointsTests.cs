using System.Net;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Features.Search;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;
using SolarOps.Api.Tests.Integration.Tickets;

namespace SolarOps.Api.Tests.Integration.Search;

/// <summary><c>GET /api/search</c>: grouped hits for the global search box, limited by role and customer scope.</summary>
public sealed class SearchEndpointsTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Url = "/api/search";

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    /// <summary>Customer A "Ján Novák" and B "Ján Kováč" (same first name), each with an installation, a ticket and a quote.</summary>
    private async Task<SearchSeed> SeedAsync()
    {
        var seed = await TicketTestApi.SeedAsync(_factory);
        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id) with { Title = "Porucha invertora A" });
        await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationB.Id) with { Title = "Porucha invertora B" });

        var by = Guid.CreateVersion7();
        Quote NewQuote(Guid customerId) => Quote.Create(
            customerId,
            [new QuoteLineData(Guid.CreateVersion7(), ComponentType.Panel, "PAN-1", "Panel", 100m, 1m)],
            23m, null, by, DateTimeOffset.UtcNow);
        var quoteA = NewQuote(seed.CustomerA.Id);
        var quoteB = NewQuote(seed.CustomerB.Id);
        await _factory.ExecuteDbAsync(async db =>
        {
            db.AddRange(quoteA, quoteB);
            await db.SaveChangesAsync();
        });
        return new SearchSeed(seed, quoteA.Id, quoteB.Id);
    }

    private sealed record SearchSeed(TicketSeed Tickets, Guid QuoteA, Guid QuoteB)
    {
        public Guid UserA => Tickets.UserA;

        public SolarOps.Api.Features.Customers.CustomerResponse CustomerA => Tickets.CustomerA;

        public SolarOps.Api.Features.Installations.InstallationResponse InstallationA => Tickets.InstallationA;
    }

    private HttpClient As(string role) => _factory.CreateApiClient(Guid.CreateVersion7(), role);

    private static Task<SearchResponse> SearchAsync(HttpClient client, string q, int? limit = null) =>
        client.GetJsonAsync<SearchResponse>($"{Url}?q={Uri.EscapeDataString(q)}" + (limit is { } l ? $"&limit={l}" : string.Empty));

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync($"{Url}?q=novak");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UserWithoutAnyRole_Returns403()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7());

        using var response = await client.GetAsync($"{Url}?q=novak");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("", "q")]
    [InlineData("a", "q")]
    [InlineData("%20%20%20", "q")]
    [InlineData("ab&limit=0", "limit")]
    [InlineData("ab&limit=11", "limit")]
    public async Task InvalidQuery_Returns400WithFieldName(string query, string field)
    {
        using var client = As(Roles.Admin);

        using var response = await client.GetAsync($"{Url}?q={query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Fact]
    public async Task MissingQuery_Returns400()
    {
        using var client = As(Roles.Admin);

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_GetsAllFourGroups()
    {
        var seed = await SeedAsync();
        using var client = As(Roles.Admin);

        var result = await SearchAsync(client, "NOVÁK");

        var customer = Assert.Single(result.Customers!);
        Assert.Equal(seed.CustomerA.Id, customer.Id);
        Assert.Equal("Ján Novák", customer.Title);
        Assert.Equal(seed.CustomerA.Email, customer.Subtitle);
        var quote = Assert.Single(result.Quotes!);
        Assert.Equal(seed.QuoteA, quote.Id);
        Assert.Equal("Ján Novák", quote.Title);
        Assert.Equal(seed.InstallationA.Id, Assert.Single(result.Installations!).Id);
        var ticket = Assert.Single(result.Tickets!);
        Assert.Equal("Porucha invertora A", ticket.Title);
        Assert.Contains("Ján Novák", ticket.Subtitle);
    }

    [Fact]
    public async Task Search_Company_ReturnsItsBusinessName()
    {
        using var client = As(Roles.Admin);
        var company = await CustomerTestData.CreateAsync(client, CustomerTestData.Company(companyName: "Slnko s.r.o."));

        var result = await SearchAsync(client, "slnko");

        var hit = Assert.Single(result.Customers!);
        Assert.Equal(company.Id, hit.Id);
        Assert.Equal("Slnko s.r.o.", hit.Title);
        Assert.Equal(company.Email, hit.Subtitle);
    }

    [Fact]
    public async Task Search_MatchesByTicketTitleAndInstallationName()
    {
        await SeedAsync();
        using var client = As(Roles.Operator);

        var byTicket = await SearchAsync(client, "invertora b");
        var byInstallation = await SearchAsync(client, "rodinný dom");

        Assert.Equal(["Porucha invertora B"], byTicket.Tickets!.Select(t => t.Title));
        Assert.Equal(2, byInstallation.Installations!.Count);
    }

    [Fact]
    public async Task Limit_CapsEveryGroup()
    {
        await SeedAsync();
        using var client = As(Roles.Admin);

        var result = await SearchAsync(client, "Ján", limit: 1);

        Assert.Single(result.Customers!);
        Assert.Single(result.Quotes!);
        Assert.Single(result.Installations!);
        Assert.Single(result.Tickets!);
    }

    [Fact]
    public async Task DefaultLimit_IsFive()
    {
        var seed = await SeedAsync();
        using var op = As(Roles.Operator);
        for (var i = 0; i < 6; i++)
        {
            await op.CreateTicketAsync(TicketTestApi.NewTicket(seed.InstallationA.Id) with { Title = $"Hluk {i}" });
        }

        var result = await SearchAsync(op, "hluk");

        Assert.Equal(5, result.Tickets!.Count);
    }

    [Theory]
    [InlineData(Roles.Admin, true, true, true, true)]
    [InlineData(Roles.Sales, true, true, true, true)]
    [InlineData(Roles.Designer, true, true, true, true)]
    [InlineData(Roles.Operator, true, true, true, true)]
    [InlineData(Roles.Technician, true, false, true, true)]
    public async Task Groups_AreReturnedOnlyWhenTheRoleMayReadThem(string role, bool customers, bool quotes, bool installations, bool tickets)
    {
        await SeedAsync();
        using var client = As(role);

        var result = await SearchAsync(client, "Ján");

        Assert.Equal(customers, result.Customers is not null);
        Assert.Equal(quotes, result.Quotes is not null);
        Assert.Equal(installations, result.Installations is not null);
        Assert.Equal(tickets, result.Tickets is not null);
    }

    [Fact]
    public async Task Customer_SeesOnlyOwnInstallationsAndTickets_NeverCustomersOrQuotes()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateApiClient(seed.UserA, Roles.Customer);

        var result = await SearchAsync(client, "Ján");

        Assert.Null(result.Customers);
        Assert.Null(result.Quotes);
        Assert.Equal(seed.InstallationA.Id, Assert.Single(result.Installations!).Id);
        Assert.Equal("Porucha invertora A", Assert.Single(result.Tickets!).Title);
    }

    [Fact]
    public async Task Customer_CannotFindForeignDataBySearching()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateApiClient(seed.UserA, Roles.Customer);

        var result = await SearchAsync(client, "Kováč");
        var byTitle = await SearchAsync(client, "invertora B");

        Assert.Empty(result.Installations!);
        Assert.Empty(result.Tickets!);
        Assert.Empty(byTitle.Tickets!);
    }

    [Fact]
    public async Task CustomerAccountWithoutLinkedCustomer_GetsEmptyGroups()
    {
        await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        var result = await SearchAsync(client, "Ján");

        Assert.Empty(result.Installations!);
        Assert.Empty(result.Tickets!);
    }

    [Fact]
    public async Task Wildcards_AreMatchedLiterally()
    {
        await SeedAsync();
        using var client = As(Roles.Admin);

        var result = await SearchAsync(client, "%%");

        Assert.Empty(result.Customers!);
        Assert.Empty(result.Quotes!);
        Assert.Empty(result.Installations!);
        Assert.Empty(result.Tickets!);
    }
}
