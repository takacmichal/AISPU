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

[Collection(PostgresCollection.Name)]
public sealed class QuoteEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales(Guid? userId = null) => _factory.CreateApiClient(userId ?? Guid.CreateVersion7(), Roles.Sales);

    private static async Task<ComponentResponse> CreateComponentAsync(
        HttpClient client, ComponentType type, string code, decimal price, int? power = null)
    {
        using var response = await client.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = type,
            Code = code,
            Name = $"Komponent {code}",
            PowerW = power,
            UnitPrice = price,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<ComponentResponse>();
    }

    private sealed record Setup(CustomerResponse Customer, ComponentResponse Panel, ComponentResponse Inverter, ComponentResponse Labor);

    private static async Task<Setup> SetupAsync(HttpClient client) => new(
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person()),
        await CreateComponentAsync(client, ComponentType.Panel, "PAN-450", 120.50m, 450),
        await CreateComponentAsync(client, ComponentType.Inverter, "INV-5K", 950m, 5000),
        await CreateComponentAsync(client, ComponentType.Labor, "LAB-MONT", 35m));

    private static object Request(Guid customerId, object[] lines, decimal? vat = null) =>
        new { customerId, lines, vatRatePercent = vat, note = "Ponuka" };

    private static object Line(Guid componentId, decimal quantity) => new { componentId, quantity };

    [Fact]
    public async Task Create_ComputesTotalsOnServer_StartsAsDraft_AndRecordsCreator()
    {
        var userId = Guid.CreateVersion7();
        using var client = Sales(userId);
        var s = await SetupAsync(client);

        using var response = await client.PostJsonAsync("/api/quotes", Request(
            s.Customer.Id, [Line(s.Panel.Id, 10), Line(s.Inverter.Id, 1), Line(s.Labor.Id, 8.5m)]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var quote = await response.ReadJsonAsync<QuoteResponse>();
        Assert.Equal($"/api/quotes/{quote.Id}", response.Headers.Location?.ToString());
        Assert.Equal(QuoteStatus.Draft, quote.Status);
        Assert.Equal(23m, quote.VatRatePercent);
        Assert.Equal(2452.50m, quote.NetTotal);
        Assert.Equal(564.08m, quote.VatTotal);
        Assert.Equal(3016.58m, quote.GrossTotal);
        Assert.Equal("EUR", quote.Currency);
        Assert.Equal(userId, quote.CreatedBy);
        Assert.Equal(["PAN-450", "INV-5K", "LAB-MONT"], quote.Lines.Select(l => l.Code));
        Assert.Equal([1205.00m, 950.00m, 297.50m], quote.Lines.Select(l => l.NetPrice));

        var fetched = await client.GetJsonAsync<QuoteResponse>(response.Headers.Location!.ToString());
        Assert.Equal(quote.GrossTotal, fetched.GrossTotal);
        Assert.Equal(3, fetched.Lines.Count);
    }

    [Fact]
    public async Task Create_WithCustomVatRate_UsesIt()
    {
        using var client = Sales();
        var s = await SetupAsync(client);

        var quote = await (await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Inverter.Id, 2)], vat: 5m)))
            .ReadJsonAsync<QuoteResponse>();

        Assert.Equal(1900m, quote.NetTotal);
        Assert.Equal(95m, quote.VatTotal);
        Assert.Equal(1995m, quote.GrossTotal);
    }

    [Fact]
    public async Task Create_IgnoresPricesSentByClient()
    {
        using var client = Sales();
        var s = await SetupAsync(client);

        using var response = await client.PostJsonAsync("/api/quotes", new
        {
            customerId = s.Customer.Id,
            lines = new[] { new { componentId = s.Panel.Id, quantity = 1, unitPrice = 1m, netPrice = 1m } },
            netTotal = 1m,
        });

        var quote = await response.ReadJsonAsync<QuoteResponse>();
        Assert.Equal(120.50m, quote.Lines.Single().UnitPrice);
        Assert.Equal(120.50m, quote.NetTotal);
    }

    [Fact]
    public async Task PriceChange_DoesNotAlterIssuedQuote_ButAppliesToNewOnes()
    {
        using var client = Sales();
        var s = await SetupAsync(client);
        var first = await (await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 10)])))
            .ReadJsonAsync<QuoteResponse>();

        using var update = await client.PutJsonAsync($"/api/components/{s.Panel.Id}", new UpdateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = "PAN-450",
            Name = "Komponent PAN-450",
            PowerW = 450,
            UnitPrice = 99m,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var reloaded = await client.GetJsonAsync<QuoteResponse>($"/api/quotes/{first.Id}");
        Assert.Equal(1205.00m, reloaded.NetTotal);
        Assert.Equal(120.50m, reloaded.Lines.Single().UnitPrice);

        var second = await (await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 10)])))
            .ReadJsonAsync<QuoteResponse>();
        Assert.Equal(990.00m, second.NetTotal);
    }

    [Fact]
    public async Task Create_InactiveComponent_Returns409()
    {
        using var client = Sales();
        var s = await SetupAsync(client);
        (await client.PostAsync($"/api/components/{s.Panel.Id}/deactivate", null)).EnsureSuccessStatusCode();

        using var response = await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 1)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownComponentOrCustomer_Returns400()
    {
        using var client = Sales();
        var s = await SetupAsync(client);

        using var unknownComponent = await client.PostJsonAsync("/api/quotes",
            Request(s.Customer.Id, [Line(s.Panel.Id, 1), Line(Guid.CreateVersion7(), 1)]));
        Assert.Equal(HttpStatusCode.BadRequest, unknownComponent.StatusCode);
        var problem = await unknownComponent.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("Lines[1].ComponentId", problem.Errors.Keys);

        using var unknownCustomer = await client.PostJsonAsync("/api/quotes", Request(Guid.CreateVersion7(), [Line(s.Panel.Id, 1)]));
        Assert.Equal(HttpStatusCode.BadRequest, unknownCustomer.StatusCode);
        Assert.Contains("CustomerId", (await unknownCustomer.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);
    }

    [Fact]
    public async Task Create_ArchivedCustomer_Returns409()
    {
        using var client = Sales();
        var s = await SetupAsync(client);
        (await client.PostAsync($"/api/customers/{s.Customer.Id}/archive", null)).EnsureSuccessStatusCode();

        using var response = await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 1)]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_InvalidQuantityOrVatOrEmptyLines_Returns400()
    {
        using var client = Sales();
        var s = await SetupAsync(client);

        using var fractionalPanel = await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 2.5m)]));
        Assert.Equal(HttpStatusCode.BadRequest, fractionalPanel.StatusCode);
        Assert.Contains("Lines[0].Quantity", (await fractionalPanel.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);

        using var zero = await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 0)]));
        Assert.Equal(HttpStatusCode.BadRequest, zero.StatusCode);

        using var badVat = await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 1)], vat: 101m));
        Assert.Equal(HttpStatusCode.BadRequest, badVat.StatusCode);
        Assert.Contains("VatRatePercent", (await badVat.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);

        using var noLines = await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, []));
        Assert.Equal(HttpStatusCode.BadRequest, noLines.StatusCode);
        Assert.Contains("Lines", (await noLines.ReadJsonAsync<HttpValidationProblemDetails>()).Errors.Keys);

        using var missing = await client.PostJsonAsync("/api/quotes", new { });
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task List_FiltersByCustomerAndStatus_NewestFirst()
    {
        using var client = Sales();
        var s = await SetupAsync(client);
        var other = await CustomerTestData.CreateAsync(client, CustomerTestData.Company());
        var first = await (await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 1)]))).ReadJsonAsync<QuoteResponse>();
        var second = await (await client.PostJsonAsync("/api/quotes", Request(s.Customer.Id, [Line(s.Panel.Id, 2)]))).ReadJsonAsync<QuoteResponse>();
        await client.PostJsonAsync("/api/quotes", Request(other.Id, [Line(s.Panel.Id, 3)]));

        var mine = await client.GetJsonAsync<List<QuoteResponse>>($"/api/quotes?customerId={s.Customer.Id}");
        Assert.Equal([second.Id, first.Id], mine.Select(q => q.Id));

        Assert.Equal(3, (await client.GetJsonAsync<List<QuoteResponse>>("/api/quotes?status=Draft")).Count);
        Assert.Empty(await client.GetJsonAsync<List<QuoteResponse>>("/api/quotes?status=Sent"));
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using var client = Sales();

        using var response = await client.GetAsync($"/api/quotes/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostJsonAsync("/api/quotes", new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task Create_WithoutSalesOrAdminRole_Returns403(string role)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var response = await client.PostJsonAsync("/api/quotes", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Read_AllowedForDesigner_ForbiddenForCustomer()
    {
        using var designer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync("/api/quotes")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/quotes")).StatusCode);
    }
}
