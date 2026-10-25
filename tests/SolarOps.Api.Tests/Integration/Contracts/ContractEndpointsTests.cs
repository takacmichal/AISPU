using System.Net;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.Contracts;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Quotes;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Contracts;

[Collection(PostgresCollection.Name)]
public sealed class ContractEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales(Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), Roles.Sales);

    private static async Task<CustomerResponse> NewCustomerAsync(HttpClient client) =>
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person($"{Guid.NewGuid():N}@example.sk"));

    /// <summary>Creates a quote of two panels (100 each) and one hour of labor (35) and moves it to <paramref name="target"/>.</summary>
    private static async Task<QuoteResponse> NewQuoteAsync(HttpClient client, CustomerResponse customer, QuoteStatus target = QuoteStatus.Accepted)
    {
        using var panelResponse = await client.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = $"PAN-{Guid.NewGuid():N}",
            Name = "Panel 450 W",
            PowerW = 450,
            UnitPrice = 100m,
        });
        var panel = await panelResponse.ReadJsonAsync<ComponentResponse>();
        using var laborResponse = await client.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = ComponentType.Labor,
            Code = $"LAB-{Guid.NewGuid():N}",
            Name = "Montáž",
            UnitPrice = 35m,
        });
        var labor = await laborResponse.ReadJsonAsync<ComponentResponse>();

        using var created = await client.PostJsonAsync("/api/quotes", new
        {
            customerId = customer.Id,
            lines = new[] { new { componentId = panel.Id, quantity = 2m }, new { componentId = labor.Id, quantity = 1m } },
        });
        var quote = await created.ReadJsonAsync<QuoteResponse>();

        if (target != QuoteStatus.Draft)
        {
            (await client.PostAsync($"/api/quotes/{quote.Id}/send", null)).EnsureSuccessStatusCode();
        }

        if (target == QuoteStatus.Accepted)
        {
            (await client.PostAsync($"/api/quotes/{quote.Id}/accept", null)).EnsureSuccessStatusCode();
        }
        else if (target == QuoteStatus.Rejected)
        {
            (await client.PostAsync($"/api/quotes/{quote.Id}/reject", null)).EnsureSuccessStatusCode();
        }

        return await client.GetJsonAsync<QuoteResponse>($"/api/quotes/{quote.Id}");
    }

    private static Task<HttpResponseMessage> CreateContractAsync(HttpClient client, Guid quoteId) =>
        client.PostJsonAsync("/api/contracts", new { quoteId });

    [Fact]
    public async Task Create_FromAcceptedQuote_CopiesCustomerLinesAndPrice()
    {
        var userId = Guid.CreateVersion7();
        using var client = Sales(userId);
        var customer = await NewCustomerAsync(client);
        var quote = await NewQuoteAsync(client, customer);

        using var response = await CreateContractAsync(client, quote.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var contract = await response.ReadJsonAsync<ContractResponse>();
        Assert.Equal($"/api/contracts/{contract.Id}", response.Headers.Location?.ToString());
        Assert.Equal(ContractStatus.Draft, contract.Status);
        Assert.Equal(customer.Id, contract.CustomerId);
        Assert.Equal(quote.Id, contract.QuoteId);
        Assert.Equal(quote.VatRatePercent, contract.VatRatePercent);
        Assert.Equal(235m, contract.NetTotal);
        Assert.Equal(quote.VatTotal, contract.VatTotal);
        Assert.Equal(quote.GrossTotal, contract.GrossTotal);
        Assert.Equal("EUR", contract.Currency);
        Assert.Equal(userId, contract.CreatedBy);
        Assert.Equal(quote.Lines.Select(l => (l.Position, l.ComponentId, l.Code, l.Name, l.UnitPrice, l.Quantity, l.NetPrice)),
            contract.Lines.Select(l => (l.Position, l.ComponentId, l.Code, l.Name, l.UnitPrice, l.Quantity, l.NetPrice)));

        var fetched = await client.GetJsonAsync<ContractResponse>(response.Headers.Location!.ToString());
        Assert.Equal(contract.Number, fetched.Number);
        Assert.Equal(2, fetched.Lines.Count);
    }

    [Theory]
    [InlineData(QuoteStatus.Draft)]
    [InlineData(QuoteStatus.Sent)]
    [InlineData(QuoteStatus.Rejected)]
    public async Task Create_FromQuoteThatIsNotAccepted_Returns409(QuoteStatus status)
    {
        using var client = Sales();
        var quote = await NewQuoteAsync(client, await NewCustomerAsync(client), status);

        using var response = await CreateContractAsync(client, quote.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Empty(await client.GetJsonAsync<List<ContractResponse>>("/api/contracts"));
    }

    [Fact]
    public async Task Create_SecondContractForTheSameQuote_Returns409()
    {
        using var client = Sales();
        var quote = await NewQuoteAsync(client, await NewCustomerAsync(client));
        (await CreateContractAsync(client, quote.Id)).EnsureSuccessStatusCode();

        using var response = await CreateContractAsync(client, quote.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await client.GetJsonAsync<List<ContractResponse>>("/api/contracts"));
    }

    [Fact]
    public async Task Create_NumbersAreUniqueAndIncreasing()
    {
        using var client = Sales();
        var contracts = new List<ContractResponse>();
        for (var i = 0; i < 3; i++)
        {
            var quote = await NewQuoteAsync(client, await NewCustomerAsync(client));
            using var response = await CreateContractAsync(client, quote.Id);
            contracts.Add(await response.ReadJsonAsync<ContractResponse>());
        }

        Assert.Equal(3, contracts.Select(c => c.Number).Distinct().Count());
        Assert.Equal(contracts.Select(c => c.Number).Order(StringComparer.Ordinal), contracts.Select(c => c.Number));
        Assert.Matches(@"^ZM-\d{4}-\d{6}$", contracts[0].Number);
    }

    [Fact]
    public async Task Create_ConcurrentRequestsForTheSameQuote_CreateOnlyOneContract()
    {
        using var client = Sales();
        var quote = await NewQuoteAsync(client, await NewCustomerAsync(client));

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => CreateContractAsync(client, quote.Id)));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.Conflict, r.StatusCode));
        Assert.Single(await client.GetJsonAsync<List<ContractResponse>>("/api/contracts"));
    }

    [Fact]
    public async Task Create_ContractKeepsQuotePrices_WhenCatalogPriceChangesLater()
    {
        using var client = Sales();
        var quote = await NewQuoteAsync(client, await NewCustomerAsync(client));
        var line = quote.Lines[0];
        (await client.PutJsonAsync($"/api/components/{line.ComponentId}", new UpdateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = line.Code,
            Name = line.Name,
            PowerW = 450,
            UnitPrice = 1m,
        })).EnsureSuccessStatusCode();

        var contract = await (await CreateContractAsync(client, quote.Id)).ReadJsonAsync<ContractResponse>();

        Assert.Equal(100m, contract.Lines[0].UnitPrice);
        Assert.Equal(235m, contract.NetTotal);
    }

    [Fact]
    public async Task Create_ArchivedCustomer_Returns409()
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);
        var quote = await NewQuoteAsync(client, customer);
        (await client.PostAsync($"/api/customers/{customer.Id}/archive", null)).EnsureSuccessStatusCode();

        using var response = await CreateContractAsync(client, quote.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownOrMissingQuote_Returns400()
    {
        using var client = Sales();

        using var unknown = await CreateContractAsync(client, Guid.CreateVersion7());
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Contains("QuoteId", (await unknown.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);

        using var missing = await client.PostJsonAsync("/api/contracts", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByCustomerQuoteAndStatus_NewestFirst()
    {
        using var client = Sales();
        var customerA = await NewCustomerAsync(client);
        var customerB = await NewCustomerAsync(client);
        var quoteA1 = await NewQuoteAsync(client, customerA);
        var quoteA2 = await NewQuoteAsync(client, customerA);
        var quoteB = await NewQuoteAsync(client, customerB);
        var first = await (await CreateContractAsync(client, quoteA1.Id)).ReadJsonAsync<ContractResponse>();
        var second = await (await CreateContractAsync(client, quoteA2.Id)).ReadJsonAsync<ContractResponse>();
        await CreateContractAsync(client, quoteB.Id);

        var ofA = await client.GetJsonAsync<List<ContractResponse>>($"/api/contracts?customerId={customerA.Id}");
        Assert.Equal([second.Id, first.Id], ofA.Select(c => c.Id));
        var byQuote = await client.GetJsonAsync<List<ContractResponse>>($"/api/contracts?quoteId={quoteA1.Id}");
        Assert.Equal([first.Id], byQuote.Select(c => c.Id));
        Assert.Equal(3, (await client.GetJsonAsync<List<ContractResponse>>("/api/contracts?status=Draft")).Count);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using var client = Sales();

        using var response = await client.GetAsync($"/api/contracts/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostJsonAsync("/api/contracts", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task Create_WithoutSalesOrAdminRole_Returns403(string role)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var response = await client.PostJsonAsync("/api/contracts", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Read_AllowedForDesigner_ForbiddenForCustomer()
    {
        using var designer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync("/api/contracts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/contracts")).StatusCode);
    }
}
