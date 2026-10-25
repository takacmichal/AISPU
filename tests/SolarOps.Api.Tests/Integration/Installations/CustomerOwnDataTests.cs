using System.Net;
using Npgsql;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Installations;

/// <summary>A customer account sees only the installations of its own customer (#137).</summary>
[Collection(PostgresCollection.Name)]
public sealed class CustomerOwnDataTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private sealed record Seed(
        Guid UserA,
        Guid UserB,
        CustomerResponse CustomerA,
        CustomerResponse CustomerB,
        InstallationResponse A1,
        InstallationResponse A2,
        InstallationResponse B1);

    private async Task<Guid> NewUserAsync()
    {
        var user = new AppUser { UserName = $"u-{Guid.NewGuid():N}@example.sk", Email = $"u-{Guid.NewGuid():N}@example.sk" };
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Users.Add(user);
            await db.SaveChangesAsync();
        });
        return user.Id;
    }

    private async Task<Seed> SeedAsync()
    {
        var userA = await NewUserAsync();
        var userB = await NewUserAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var customerA = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("a@example.sk") with { UserId = userA });
        var customerB = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("b@example.sk", lastName: "Kováč") with { UserId = userB });

        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        async Task<InstallationResponse> NewAsync(Guid customerId) =>
            (await op.CreateInstallationAsync(InstallationsTestData.NewInstallationRequest() with { CustomerId = customerId })).Installation;

        return new Seed(userA, userB, customerA, customerB, await NewAsync(customerA.Id), await NewAsync(customerA.Id), await NewAsync(customerB.Id));
    }

    private HttpClient Customer(Guid userId) => _factory.CreateApiClient(userId, Roles.Customer);

    [Fact]
    public async Task List_ReturnsOnlyOwnInstallations()
    {
        var s = await SeedAsync();
        using var client = Customer(s.UserA);

        var list = await client.GetJsonAsync<List<InstallationResponse>>("/api/installations");

        Assert.Equal([s.A1.Id, s.A2.Id], list.Select(i => i.Id).Order());
        Assert.All(list, i => Assert.Equal(s.CustomerA.Id, i.CustomerId));
    }

    [Fact]
    public async Task List_FiltersCannotWidenTheScope()
    {
        var s = await SeedAsync();
        using var client = Customer(s.UserA);

        var byForeignCustomer = await client.GetJsonAsync<List<InstallationResponse>>($"/api/installations?customerId={s.CustomerB.Id}");
        var byForeignContract = await client.GetJsonAsync<List<InstallationResponse>>($"/api/installations?contractId={s.B1.ContractId}");
        var byForeignSupplyPoint = await client.GetJsonAsync<List<InstallationResponse>>($"/api/installations?supplyPointId={s.B1.SupplyPointId}");
        var ownByState = await client.GetJsonAsync<List<InstallationResponse>>("/api/installations?state=Planned");

        Assert.Empty(byForeignCustomer);
        Assert.Empty(byForeignContract);
        Assert.Empty(byForeignSupplyPoint);
        Assert.Equal(2, ownByState.Count);
    }

    [Fact]
    public async Task Get_OwnInstallation_Returns200()
    {
        var s = await SeedAsync();
        using var client = Customer(s.UserA);

        var detail = await client.GetJsonAsync<InstallationDetailResponse>($"/api/installations/{s.A1.Id}");

        Assert.Equal(s.A1.Id, detail.Installation.Id);
    }

    [Fact]
    public async Task Get_ForeignInstallation_Returns404_LikeAMissingOne()
    {
        var s = await SeedAsync();
        using var client = Customer(s.UserA);

        using var foreign = await client.GetAsync($"/api/installations/{s.B1.Id}");
        using var missing = await client.GetAsync($"/api/installations/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task EachCustomerSeesOnlyItsOwn()
    {
        var s = await SeedAsync();
        using var clientB = Customer(s.UserB);

        var list = await clientB.GetJsonAsync<List<InstallationResponse>>("/api/installations");

        Assert.Equal([s.B1.Id], list.Select(i => i.Id));
        using var foreign = await clientB.GetAsync($"/api/installations/{s.A1.Id}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task CustomerAccountWithoutLinkedCustomer_SeesNothing()
    {
        var s = await SeedAsync();
        using var client = Customer(await NewUserAsync());

        Assert.Empty(await client.GetJsonAsync<List<InstallationResponse>>("/api/installations"));
        using var response = await client.GetAsync($"/api/installations/{s.A1.Id}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ArchivedCustomer_StillSeesItsInstallations()
    {
        var s = await SeedAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        (await sales.PostAsync($"/api/customers/{s.CustomerA.Id}/archive", null)).EnsureSuccessStatusCode();
        using var client = Customer(s.UserA);

        Assert.Equal(2, (await client.GetJsonAsync<List<InstallationResponse>>("/api/installations")).Count);
    }

    [Theory]
    [InlineData(Roles.Operator)]
    [InlineData(Roles.Sales)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Admin)]
    public async Task BackOfficeRoles_SeeAllInstallations(string role)
    {
        var s = await SeedAsync();
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        var list = await client.GetJsonAsync<List<InstallationResponse>>("/api/installations");

        Assert.Equal(3, list.Count);
        using var foreign = await client.GetAsync($"/api/installations/{s.B1.Id}");
        Assert.Equal(HttpStatusCode.OK, foreign.StatusCode);
    }

    [Fact]
    public async Task UserWithCustomerAndBackOfficeRole_IsNotRestricted()
    {
        var s = await SeedAsync();
        using var client = _factory.CreateApiClient(s.UserA, Roles.Customer, Roles.Sales);

        Assert.Equal(3, (await client.GetJsonAsync<List<InstallationResponse>>("/api/installations")).Count);
    }

    [Fact]
    public async Task Customer_CannotChangeInstallations()
    {
        var s = await SeedAsync();
        using var client = Customer(s.UserA);
        var id = s.A1.Id;

        using var create = await client.PostJsonAsync("/api/installations", InstallationsTestData.NewInstallationRequest() with { CustomerId = s.CustomerA.Id });
        using var transition = await client.PostJsonAsync($"/api/installations/{id}/transitions", new { targetState = "ComponentsOrdered" });
        using var update = await client.PutJsonAsync($"/api/installations/{id}", new { name = "Nové meno" });

        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, transition.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
    }

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync("/api/installations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
