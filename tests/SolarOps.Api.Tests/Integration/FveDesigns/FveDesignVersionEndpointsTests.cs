using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using static SolarOps.Api.Tests.Integration.FveDesigns.FveDesignTestApi;

namespace SolarOps.Api.Tests.Integration.FveDesigns;

[Collection(PostgresCollection.Name)]
public sealed class FveDesignVersionEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);
    private readonly Guid _designerId = Guid.CreateVersion7();

    public void Dispose()
    {
        _factory.Dispose();
        NpgsqlConnection.ClearAllPools();
    }

    private HttpClient Designer() => _factory.CreateApiClient(_designerId, Roles.Designer);

    private HttpClient Admin() => _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Admin);

    private static async Task<ComponentResponse> CreateComponentAsync(HttpClient admin, ComponentType type, string code, int power, decimal price)
    {
        using var response = await admin.PostJsonAsync("/api/components", new CreateComponentRequest
        {
            Type = type,
            Code = code,
            Name = $"Komponent {code}",
            PowerW = type == ComponentType.Labor ? null : power,
            UnitPrice = price,
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<ComponentResponse>();
    }

    private sealed record Catalog(ComponentResponse Panel, ComponentResponse Inverter);

    private async Task<Catalog> SeedCatalogAsync()
    {
        using var admin = Admin();
        return new Catalog(
            await CreateComponentAsync(admin, ComponentType.Panel, "PAN-450", 450, 120.5m),
            await CreateComponentAsync(admin, ComponentType.Inverter, "INV-5K", 5000, 950m));
    }

    private static ConfigureDesignRequest Config(Catalog catalog, int count = 12) => new()
    {
        PanelId = catalog.Panel.Id,
        PanelCount = count,
        InverterId = catalog.Inverter.Id,
    };

    [Fact]
    public async Task Preview_CompatibleConfiguration_ReturnsEstimateAndDoesNotPersist()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var response = await client.PostJsonAsync($"/api/designs/{design.Id}/configuration/preview", Config(catalog));
        var preview = await response.ReadAsync<DesignConfigurationPreviewResponse>(HttpStatusCode.OK);

        Assert.True(preview.Compatibility.IsCompatible);
        Assert.Equal(5.4, preview.Compatibility.DcPowerKwp, 3);
        Assert.Equal(1.08, preview.Compatibility.DcAcRatio, 3);
        Assert.NotNull(preview.Estimate);
        Assert.True(preview.Estimate.AnnualKwh > 4000);
        Assert.Equal(0, await _factory.ExecuteDbAsync(db => db.Set<FveDesignVersion>().CountAsync()));
    }

    [Fact]
    public async Task Preview_IncompatibleConfiguration_Returns200WithErrors()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var response = await client.PostJsonAsync($"/api/designs/{design.Id}/configuration/preview", Config(catalog, 40));
        var preview = await response.ReadAsync<DesignConfigurationPreviewResponse>(HttpStatusCode.OK);

        Assert.False(preview.Compatibility.IsCompatible);
        Assert.NotEmpty(preview.Compatibility.Errors);
        Assert.Null(preview.Estimate);
    }

    [Fact]
    public async Task Save_CreatesImmutableVersionsNumberedFromOne()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var first = await (await client.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog, 12)))
            .ReadAsync<FveDesignVersionResponse>(HttpStatusCode.Created);
        var second = await (await client.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog, 14)))
            .ReadAsync<FveDesignVersionResponse>(HttpStatusCode.Created);

        Assert.Equal(1, first.VersionNumber);
        Assert.Equal(2, second.VersionNumber);
        Assert.Equal(_designerId, first.CreatedBy);
        Assert.Equal("PAN-450", first.PanelCode);
        Assert.Equal(120.5m, first.PanelUnitPrice);
        Assert.True(second.AnnualYieldKwh > first.AnnualYieldKwh);

        // The first version is unchanged by saving the second one.
        var refetched = await client.GetJsonAsync<FveDesignVersionResponse>($"/api/designs/{design.Id}/versions/1");
        Assert.Equal(first.Id, refetched.Id);
        Assert.Equal(first.PanelCount, refetched.PanelCount);
        Assert.Equal(first.AnnualYieldKwh, refetched.AnnualYieldKwh);
        // The POST response carries the in-memory value (100 ns ticks); PostgreSQL stores timestamptz in microseconds.
        Assert.Equal(first.CreatedAt, refetched.CreatedAt, TimeSpan.FromMilliseconds(1));

        var list = await client.GetJsonAsync<List<FveDesignVersionResponse>>($"/api/designs/{design.Id}/versions");
        Assert.Equal([2, 1], list.Select(v => v.VersionNumber));
    }

    [Fact]
    public async Task Save_SnapshotIsKeptWhenCatalogPriceChanges()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateInDesignAsync();
        await (await client.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog))).ReadAsync<FveDesignVersionResponse>(HttpStatusCode.Created);

        using var admin = Admin();
        using var update = await admin.PutJsonAsync($"/api/components/{catalog.Panel.Id}", new UpdateComponentRequest
        {
            Type = ComponentType.Panel,
            Code = "PAN-450",
            Name = "Nový názov",
            PowerW = 450,
            UnitPrice = 200m,
        });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        var version = await client.GetJsonAsync<FveDesignVersionResponse>($"/api/designs/{design.Id}/versions/1");
        Assert.Equal(120.5m, version.PanelUnitPrice);
        Assert.Equal("Komponent PAN-450", version.PanelName);
    }

    [Fact]
    public async Task Save_IncompatibleConfiguration_Returns400AndSavesNothing()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var response = await client.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog, 40));
        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);

        Assert.Contains("PanelCount", problem.Errors.Keys);
        Assert.Equal(0, await _factory.ExecuteDbAsync(db => db.Set<FveDesignVersion>().CountAsync()));
    }

    [Fact]
    public async Task Save_MissingFields_Returns400()
    {
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var response = await client.PostJsonAsync($"/api/designs/{design.Id}/versions", new ConfigureDesignRequest());
        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);

        Assert.Contains("PanelId", problem.Errors.Keys);
        Assert.Contains("InverterId", problem.Errors.Keys);
        Assert.Contains("PanelCount", problem.Errors.Keys);
    }

    [Fact]
    public async Task Save_WrongComponentType_Returns400()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var swapped = new ConfigureDesignRequest { PanelId = catalog.Inverter.Id, PanelCount = 12, InverterId = catalog.Panel.Id };
        var response = await client.PostJsonAsync($"/api/designs/{design.Id}/versions", swapped);
        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);

        Assert.Contains("PanelId", problem.Errors.Keys);
        Assert.Contains("InverterId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Save_UnknownComponent_Returns404()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var response = await client.PostJsonAsync(
            $"/api/designs/{design.Id}/versions", Config(catalog) with { PanelId = Guid.CreateVersion7() });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Save_InactiveComponent_Returns409()
    {
        var catalog = await SeedCatalogAsync();
        using (var admin = Admin())
        {
            using var deactivate = await admin.PostAsync($"/api/components/{catalog.Inverter.Id}/deactivate", null);
            Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);
        }

        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var response = await client.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Save_DesignNotInDesign_Returns409()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();
        var design = await client.CreateDesignAsync();

        var response = await client.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Save_UnknownDesign_Returns404()
    {
        var catalog = await SeedCatalogAsync();
        using var client = Designer();

        var response = await client.PostJsonAsync($"/api/designs/{Guid.CreateVersion7()}/versions", Config(catalog));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Save_AsSales_Returns403_ButSalesCanRead()
    {
        var catalog = await SeedCatalogAsync();
        using var designer = Designer();
        var design = await designer.CreateInDesignAsync();
        await (await designer.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog))).ReadAsync<FveDesignVersionResponse>(HttpStatusCode.Created);

        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);
        var denied = await sales.PostJsonAsync($"/api/designs/{design.Id}/versions", Config(catalog));
        var list = await sales.GetJsonAsync<List<FveDesignVersionResponse>>($"/api/designs/{design.Id}/versions");

        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Single(list);
    }

    [Fact]
    public async Task Get_UnknownVersionNumber_Returns404()
    {
        using var client = Designer();
        var design = await client.CreateInDesignAsync();

        var response = await client.GetAsync($"/api/designs/{design.Id}/versions/7");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
