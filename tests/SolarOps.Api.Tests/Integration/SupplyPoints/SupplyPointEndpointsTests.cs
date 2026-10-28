using System.Net;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.SupplyPoints;

[Collection(PostgresCollection.Name)]
public sealed class SupplyPointEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private const string Eic = "24ZSS1234567890A";

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose() => _factory.Dispose();

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    private static CreateSupplyPointRequest NewRequest(string eic = Eic) => new()
    {
        Name = "Rodinný dom",
        EicCode = eic,
        Address = CustomerTestData.Address(),
        MainBreakerAmps = 25,
    };

    private static string Url(Guid customerId) => $"/api/customers/{customerId}/supply-points";

    private static async Task<SupplyPointResponse> CreateAsync(HttpClient client, Guid customerId, CreateSupplyPointRequest request)
    {
        using var response = await client.PostJsonAsync(Url(customerId), request);
        response.EnsureSuccessStatusCode();
        return await response.ReadJsonAsync<SupplyPointResponse>();
    }

    private static async Task<CustomerResponse> NewCustomerAsync(HttpClient client, string email = "jan.novak@example.sk") =>
        await CustomerTestData.CreateAsync(client, CustomerTestData.Person(email));

    [Fact]
    public async Task Create_Returns201_AndCustomerCanHaveSeveralSupplyPoints()
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);

        using var response = await client.PostJsonAsync(Url(customer.Id), NewRequest());
        await CreateAsync(client, customer.Id, NewRequest("24ZSS0000000001B"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync<SupplyPointResponse>();
        Assert.Equal($"{Url(customer.Id)}/{created.Id}", response.Headers.Location?.ToString());
        Assert.Equal(25, created.MainBreakerAmps);
        Assert.Equal("SK", created.Address.Country);

        var list = await client.GetJsonAsync<List<SupplyPointResponse>>(Url(customer.Id));
        Assert.Equal(2, list.Count);
    }

    [Theory]
    [InlineData("24zss 1234 5678 90a", "24ZSS1234567890A")]
    public async Task Create_NormalizesEic(string input, string expected)
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);

        var created = await CreateAsync(client, customer.Id, NewRequest(input));

        Assert.Equal(expected, created.EicCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("24ZSS123")]
    [InlineData("ABZSS1234567890A")]
    [InlineData("24ZSS1234567890A1")]
    [InlineData("24ZSS12345678*0A")]
    public async Task Create_RejectsInvalidEic(string eic)
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);

        using var response = await client.PostJsonAsync(Url(customer.Id), NewRequest(eic));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("eicCode", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    public async Task Create_RejectsBreakerOutOfRange(int amps)
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);

        using var response = await client.PostJsonAsync(Url(customer.Id), NewRequest() with { MainBreakerAmps = amps });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("mainBreakerAmps", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_RejectsBadAddress()
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);

        using var response = await client.PostJsonAsync(Url(customer.Id), NewRequest() with
        {
            Address = new AddressDto { Street = "", City = "Nitra", PostalCode = "12" },
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Address.Street", body);
        Assert.Contains("Address.PostalCode", body);
    }

    [Fact]
    public async Task Create_DuplicateEic_Returns409()
    {
        using var client = Sales();
        var first = await NewCustomerAsync(client);
        var second = await NewCustomerAsync(client, "druhy@example.sk");
        await CreateAsync(client, first.Id, NewRequest());

        using var response = await client.PostJsonAsync(Url(second.Id), NewRequest());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_UnknownCustomer_Returns404()
    {
        using var client = Sales();

        using var response = await client.PostJsonAsync(Url(Guid.NewGuid()), NewRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesFields_AndKeepsOwnEic()
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);
        var created = await CreateAsync(client, customer.Id, NewRequest());

        using var response = await client.PutJsonAsync($"{Url(customer.Id)}/{created.Id}", new UpdateSupplyPointRequest
        {
            Name = "Chata",
            EicCode = Eic,
            Address = CustomerTestData.Address(),
            MainBreakerAmps = 40,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<SupplyPointResponse>();
        Assert.Equal("Chata", updated.Name);
        Assert.Equal(40, updated.MainBreakerAmps);
    }

    [Fact]
    public async Task Update_ToEicOfAnotherSupplyPoint_Returns409()
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);
        await CreateAsync(client, customer.Id, NewRequest());
        var other = await CreateAsync(client, customer.Id, NewRequest("24ZSS0000000001B"));

        using var response = await client.PutJsonAsync($"{Url(customer.Id)}/{other.Id}", new UpdateSupplyPointRequest
        {
            EicCode = Eic,
            Address = CustomerTestData.Address(),
            MainBreakerAmps = 25,
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SupplyPoint_OfAnotherCustomer_IsNotFound()
    {
        using var client = Sales();
        var owner = await NewCustomerAsync(client);
        var other = await NewCustomerAsync(client, "druhy@example.sk");
        var created = await CreateAsync(client, owner.Id, NewRequest());

        using var response = await client.GetAsync($"{Url(other.Id)}/{created.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesSupplyPoint()
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);
        var created = await CreateAsync(client, customer.Id, NewRequest());

        using var response = await client.DeleteAsync($"{Url(customer.Id)}/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await client.GetJsonAsync<List<SupplyPointResponse>>(Url(customer.Id)));
    }

    [Fact]
    public async Task Delete_WithInstallation_Returns409()
    {
        using var client = Sales();
        var customer = await NewCustomerAsync(client);
        var created = await CreateAsync(client, customer.Id, NewRequest());
        using var installation = await client.PostJsonAsync("/api/installations", new CreateInstallationRequest
        {
            CustomerId = customer.Id,
            ContractId = Guid.CreateVersion7(),
            SupplyPointId = created.Id,
            Name = "FVE Rodinný dom",
        });
        installation.EnsureSuccessStatusCode();

        using var response = await client.DeleteAsync($"{Url(customer.Id)}/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Single(await client.GetJsonAsync<List<SupplyPointResponse>>(Url(customer.Id)));
    }

    [Fact]
    public async Task Write_RequiresSalesOrAdmin()
    {
        using var sales = Sales();
        var customer = await NewCustomerAsync(sales);
        using var technician = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Technician);
        using var anonymous = _factory.CreateApiClient();

        using var forbidden = await technician.PostJsonAsync(Url(customer.Id), NewRequest());
        using var unauthorized = await anonymous.GetAsync(Url(customer.Id));
        var readable = await technician.GetJsonAsync<List<SupplyPointResponse>>(Url(customer.Id));

        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Empty(readable);
    }
}
