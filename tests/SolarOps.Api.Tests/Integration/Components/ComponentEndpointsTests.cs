using System.Net;
using Microsoft.AspNetCore.Http;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;

namespace SolarOps.Api.Tests.Integration.Components;

[Collection(PostgresCollection.Name)]
public sealed class ComponentEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Sales() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

    private static CreateComponentRequest Panel(string code = "PAN-450", string name = "Panel 450 W", decimal price = 120.50m) => new()
    {
        Type = ComponentType.Panel,
        Code = code,
        Name = name,
        Manufacturer = "JA Solar",
        PowerW = 450,
        UnitPrice = price,
    };

    private static CreateComponentRequest Inverter(string code = "INV-5K") => new()
    {
        Type = ComponentType.Inverter,
        Code = code,
        Name = "Invertor 5 kW",
        PowerW = 5000,
        UnitPrice = 950m,
    };

    private static CreateComponentRequest Labor(string code = "LAB-MONT") => new()
    {
        Type = ComponentType.Labor,
        Code = code,
        Name = "Montáž (hodina)",
        UnitPrice = 35m,
    };

    private static async Task<ComponentResponse> CreateAsync(HttpClient client, CreateComponentRequest request)
    {
        using var response = await client.PostJsonAsync("/api/components", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<ComponentResponse>();
    }

    [Fact]
    public async Task Create_Panel_Returns201WithLocation_AndNormalizedCode()
    {
        using var client = Sales();

        using var response = await client.PostJsonAsync("/api/components", Panel(" pan-450 "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var component = await response.ReadJsonAsync<ComponentResponse>();
        Assert.Equal($"/api/components/{component.Id}", response.Headers.Location?.ToString());
        Assert.Equal("PAN-450", component.Code);
        Assert.Equal(ComponentType.Panel, component.Type);
        Assert.Equal(450, component.PowerW);
        Assert.Equal(120.50m, component.UnitPrice);
        Assert.Equal("EUR", component.Currency);
        Assert.True(component.IsActive);

        var fetched = await client.GetJsonAsync<ComponentResponse>(response.Headers.Location!.ToString());
        Assert.Equal(component.Id, fetched.Id);
    }

    [Fact]
    public async Task Create_Labor_WithoutPower_Succeeds()
    {
        using var client = Sales();

        var component = await CreateAsync(client, Labor());

        Assert.Null(component.PowerW);
    }

    [Theory]
    [InlineData("PowerW", ComponentType.Panel, null, 100)]
    [InlineData("PowerW", ComponentType.Inverter, 0, 100)]
    [InlineData("PowerW", ComponentType.Labor, 100, 10)]
    [InlineData("UnitPrice", ComponentType.Panel, 450, -1)]
    [InlineData("UnitPrice", ComponentType.Panel, 450, 10.123)]
    public async Task Create_InvalidData_Returns400(string field, ComponentType type, int? power, double price)
    {
        using var client = Sales();
        var request = Panel() with { Type = type, PowerW = power, UnitPrice = (decimal)price };

        using var response = await client.PostJsonAsync("/api/components", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains(field, problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_MissingRequiredFields_Returns400()
    {
        using var client = Sales();

        using var response = await client.PostJsonAsync("/api/components", new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.ReadJsonAsync<HttpValidationProblemDetails>();
        Assert.Contains("Type", problem.Errors.Keys);
        Assert.Contains("Code", problem.Errors.Keys);
        Assert.Contains("Name", problem.Errors.Keys);
        Assert.Contains("UnitPrice", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_DuplicateCode_Returns409_CaseInsensitive()
    {
        using var client = Sales();
        await CreateAsync(client, Panel("PAN-450"));

        using var response = await client.PostJsonAsync("/api/components", Inverter("pan-450"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_ChangesPrice_AndRejectsCodeOfAnotherComponent()
    {
        using var client = Sales();
        var panel = await CreateAsync(client, Panel());
        await CreateAsync(client, Inverter());

        using var ok = await client.PutJsonAsync($"/api/components/{panel.Id}", new UpdateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = "PAN-450",
            Name = "Panel 450 W",
            PowerW = 450,
            UnitPrice = 99.90m,
        });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(99.90m, (await ok.ReadJsonAsync<ComponentResponse>()).UnitPrice);

        using var clash = await client.PutJsonAsync($"/api/components/{panel.Id}", new UpdateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = "inv-5k",
            Name = "Panel 450 W",
            PowerW = 450,
            UnitPrice = 99.90m,
        });
        Assert.Equal(HttpStatusCode.Conflict, clash.StatusCode);

        using var missing = await client.PutJsonAsync($"/api/components/{Guid.CreateVersion7()}", new UpdateComponentRequest
        {
            Type = ComponentType.Labor,
            Code = "X",
            Name = "X",
            UnitPrice = 1m,
        });
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Deactivate_HidesComponentFromActiveList_AndCanBeReactivated()
    {
        using var client = Sales();
        var panel = await CreateAsync(client, Panel());
        await CreateAsync(client, Inverter());

        using var deactivated = await client.PostAsync($"/api/components/{panel.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivated.StatusCode);
        Assert.False((await deactivated.ReadJsonAsync<ComponentResponse>()).IsActive);

        var active = await client.GetJsonAsync<List<ComponentResponse>>("/api/components?active=true");
        Assert.Equal(["INV-5K"], active.Select(c => c.Code));
        var inactive = await client.GetJsonAsync<List<ComponentResponse>>("/api/components?active=false");
        Assert.Equal(["PAN-450"], inactive.Select(c => c.Code));
        Assert.Equal(2, (await client.GetJsonAsync<List<ComponentResponse>>("/api/components")).Count);

        using var twice = await client.PostAsync($"/api/components/{panel.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);

        using var reactivated = await client.PostAsync($"/api/components/{panel.Id}/activate", null);
        Assert.True((await reactivated.ReadJsonAsync<ComponentResponse>()).IsActive);
    }

    [Fact]
    public async Task List_FiltersByTypeAndQuery_OrderedByTypeThenName()
    {
        using var client = Sales();
        await CreateAsync(client, Labor());
        await CreateAsync(client, Inverter());
        await CreateAsync(client, Panel("PAN-B", "Panel B"));
        await CreateAsync(client, Panel("PAN-A", "Panel A"));

        var all = await client.GetJsonAsync<List<ComponentResponse>>("/api/components");
        Assert.Equal(["INV-5K", "LAB-MONT", "PAN-A", "PAN-B"], all.Select(c => c.Code));

        var panels = await client.GetJsonAsync<List<ComponentResponse>>("/api/components?type=Panel");
        Assert.Equal(2, panels.Count);

        var found = await client.GetJsonAsync<List<ComponentResponse>>("/api/components?q=ja%20sol");
        Assert.Equal(2, found.Count);

        var wildcard = await client.GetJsonAsync<List<ComponentResponse>>("/api/components?q=%25");
        Assert.Empty(wildcard);
    }

    [Fact]
    public async Task Get_UnknownId_Returns404()
    {
        using var client = Sales();

        using var response = await client.GetAsync($"/api/components/{Guid.CreateVersion7()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        using var response = await client.PostJsonAsync("/api/components", Panel());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Designer)]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    public async Task Create_WithoutSalesOrAdminRole_Returns403(string role)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        using var response = await client.PostJsonAsync("/api/components", Panel());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_AllowedForDesigner_ForbiddenForCustomer()
    {
        using var designer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Designer);
        using var customer = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        Assert.Equal(HttpStatusCode.OK, (await designer.GetAsync("/api/components")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await customer.GetAsync("/api/components")).StatusCode);
    }

    [Fact]
    public async Task Admin_CanCreateComponent()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

        using var response = await client.PostJsonAsync("/api/components", Panel());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
