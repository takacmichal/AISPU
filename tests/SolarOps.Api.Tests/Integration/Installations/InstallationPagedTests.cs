using System.Net;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using SolarOps.Api.Tests.Integration.Customers;

namespace SolarOps.Api.Tests.Integration.Installations;

/// <summary><c>GET /api/installations/paged</c>: paging, filters, search, sorting, state counts and customer scope.</summary>
public sealed class InstallationPagedTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>, IDisposable
{
    private const string Url = "/api/installations/paged";

    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

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

    private sealed record Seed(
        Guid UserA,
        Guid UserB,
        CustomerResponse CustomerA,
        CustomerResponse CustomerB,
        InstallationResponse Roof,
        InstallationResponse Garage,
        InstallationResponse Farm,
        Guid Technician);

    /// <summary>
    /// Customer A (Novák): "FVE Strecha" (Planned, 5 kWp, supply point in Nitra), "FVE Garáž" (ComponentsOrdered, 9 kWp, technician).
    /// Customer B (Kováč): "FVE Farma" (Planned, 20 kWp).
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        var userA = await NewUserAsync();
        var userB = await NewUserAsync();
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var a = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("a@example.sk", lastName: "Novák") with { UserId = userA });
        var b = await CustomerTestData.CreateAsync(sales, CustomerTestData.Person("b@example.sk", lastName: "Kováč") with { UserId = userB });

        var supplyPoint = SupplyPoint.Create(a.Id, "Dom", $"24Z{Guid.NewGuid():N}"[..16], new PostalAddress("Hlavná 1", "Nitra", "949 01"), 25, DateTimeOffset.UtcNow);
        await _factory.ExecuteDbAsync(async db =>
        {
            db.Add(supplyPoint);
            await db.SaveChangesAsync();
        });

        using var op = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);
        async Task<InstallationResponse> NewAsync(Guid customerId, string name, decimal kwp, Guid? supplyPointId = null) =>
            (await op.CreateInstallationAsync(InstallationsTestData.NewInstallationRequest(kwp) with
            {
                CustomerId = customerId,
                Name = name,
                SupplyPointId = supplyPointId ?? Guid.CreateVersion7(),
            })).Installation;

        var roof = await NewAsync(a.Id, "FVE Strecha", 5m, supplyPoint.Id);
        var garage = await NewAsync(a.Id, "FVE Garáž", 9m);
        var farm = await NewAsync(b.Id, "FVE Farma", 20m);

        using var ordered = await op.PostJsonAsync($"/api/installations/{garage.Id}/transitions", new InstallationTransitionRequest { TargetState = InstallationState.ComponentsOrdered });
        ordered.EnsureSuccessStatusCode();
        var technician = await _factory.SeedTechnicianAsync();
        using var assign = await op.PutJsonAsync($"/api/installations/{garage.Id}/technicians", new AssignTechniciansRequest { TechnicianIds = [technician] });
        assign.EnsureSuccessStatusCode();

        return new Seed(userA, userB, a, b, roof, garage, farm, technician);
    }

    private HttpClient Staff() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Operator);

    private HttpClient Customer(Guid userId) => _factory.CreateApiClient(userId, Roles.Customer);

    [Fact]
    public async Task Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.GetAsync(Url);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Empty_ReturnsDefaultsAndAllStatesWithZero()
    {
        using var client = Staff();

        var page = await client.GetJsonAsync<InstallationPageResponse>(Url);

        Assert.Empty(page.Items);
        Assert.Equal((1, 20, 0), (page.Page, page.PageSize, page.TotalCount));
        Assert.Equal(Enum.GetNames<InstallationState>(), page.StatusCounts.Keys);
        Assert.All(page.StatusCounts.Values, c => Assert.Equal(0, c));
    }

    [Fact]
    public async Task List_DefaultsToNewestFirst_AndCountsPerState()
    {
        var s = await SeedAsync();
        using var client = Staff();

        var page = await client.GetJsonAsync<InstallationPageResponse>(Url);

        Assert.Equal([s.Farm.Id, s.Garage.Id, s.Roof.Id], page.Items.Select(i => i.Id));
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.StatusCounts["Planned"]);
        Assert.Equal(1, page.StatusCounts["ComponentsOrdered"]);
        Assert.Equal(0, page.StatusCounts["HandedOver"]);
    }

    [Fact]
    public async Task StateFilter_NarrowsItems_ButNotTheCounts()
    {
        var s = await SeedAsync();
        using var client = Staff();

        var page = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?state=planned");

        Assert.Equal([s.Farm.Id, s.Roof.Id], page.Items.Select(i => i.Id));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(3, page.StatusCounts.Values.Sum());
    }

    [Fact]
    public async Task CustomerAndTechnicianFilters_NarrowItemsAndCounts()
    {
        var s = await SeedAsync();
        using var client = Staff();

        var byCustomer = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?customerId={s.CustomerA.Id}");
        var byTechnician = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?technicianId={s.Technician}");

        Assert.Equal([s.Garage.Id, s.Roof.Id], byCustomer.Items.Select(i => i.Id));
        Assert.Equal(2, byCustomer.StatusCounts.Values.Sum());
        Assert.Equal([s.Garage.Id], byTechnician.Items.Select(i => i.Id));
        Assert.Equal(1, byTechnician.StatusCounts["ComponentsOrdered"]);
        Assert.Equal(0, byTechnician.StatusCounts["Planned"]);
    }

    [Theory]
    [InlineData("strecha", "roof")]
    [InlineData("GARÁŽ", "garage")]
    [InlineData("nitra", "roof")]
    [InlineData("kováč", "farm")]
    public async Task Search_MatchesNameSupplyPointAddressAndCustomerName(string q, string expected)
    {
        var s = await SeedAsync();
        var expectedId = expected switch { "roof" => s.Roof.Id, "garage" => s.Garage.Id, _ => s.Farm.Id };
        using var client = Staff();

        var page = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?q={Uri.EscapeDataString(q)}");

        Assert.Equal([expectedId], page.Items.Select(i => i.Id));
        Assert.Equal(1, page.StatusCounts.Values.Sum());
    }

    [Fact]
    public async Task Search_TreatsWildcardsLiterally()
    {
        await SeedAsync();
        using var client = Staff();

        var page = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?q=%25");

        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Paging_AndSorting()
    {
        var s = await SeedAsync();
        using var client = Staff();

        var byPowerDesc = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?sortBy=plannedPowerKwp&sortDir=desc&pageSize=2");
        var byNameSecondPage = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?sortBy=name&page=2&pageSize=2");

        Assert.Equal([s.Farm.Id, s.Garage.Id], byPowerDesc.Items.Select(i => i.Id));
        Assert.Equal(3, byPowerDesc.TotalCount);
        Assert.Equal([s.Farm.Id], byNameSecondPage.Items.Select(i => i.Id));
    }

    [Theory]
    [InlineData("sortBy=note", "sortBy")]
    [InlineData("sortDir=up", "sortDir")]
    [InlineData("state=Done", "state")]
    [InlineData("state=77", "state")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=-1", "page")]
    public async Task InvalidQuery_Returns400WithFieldName(string query, string field)
    {
        using var client = Staff();

        using var response = await client.GetAsync($"{Url}?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Fact]
    public async Task Customer_SeesOnlyOwnInstallations_AndOwnCounts()
    {
        var s = await SeedAsync();
        using var client = Customer(s.UserA);

        var page = await client.GetJsonAsync<InstallationPageResponse>(Url);

        Assert.Equal([s.Garage.Id, s.Roof.Id], page.Items.Select(i => i.Id));
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.StatusCounts.Values.Sum());
    }

    [Fact]
    public async Task Customer_CannotWidenTheScopeWithFiltersOrSearch()
    {
        var s = await SeedAsync();
        using var client = Customer(s.UserA);

        var byForeignCustomer = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?customerId={s.CustomerB.Id}");
        var bySearch = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?q=farma");
        var byTechnician = await client.GetJsonAsync<InstallationPageResponse>($"{Url}?technicianId={Guid.CreateVersion7()}");

        Assert.Empty(byForeignCustomer.Items);
        Assert.Equal(0, byForeignCustomer.StatusCounts.Values.Sum());
        Assert.Empty(bySearch.Items);
        Assert.Equal(0, bySearch.StatusCounts.Values.Sum());
        Assert.Empty(byTechnician.Items);
    }

    [Fact]
    public async Task CustomerAccountWithoutLinkedCustomer_SeesNothing()
    {
        await SeedAsync();
        using var client = Customer(await NewUserAsync());

        var page = await client.GetJsonAsync<InstallationPageResponse>(Url);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
    }

    [Fact]
    public async Task ExistingArrayEndpoint_IsUnchanged()
    {
        await SeedAsync();
        using var client = Staff();

        var all = await client.GetJsonAsync<List<InstallationResponse>>("/api/installations");

        Assert.Equal(3, all.Count);
    }
}
