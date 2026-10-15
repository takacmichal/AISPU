using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Infrastructure.Identity;
using SolarOps.Api.Tests.Infrastructure;
using static SolarOps.Api.Tests.Integration.FveDesigns.FveDesignTestApi;

namespace SolarOps.Api.Tests.Integration.FveDesigns;

[Collection(PostgresCollection.Name)]
public sealed class FveDesignEndpointsTests(PostgresFixture postgres) : IDisposable
{
    private readonly ApiFactory _factory = new(postgres);
    private readonly Guid _designerId = Guid.CreateVersion7();

    public void Dispose() => _factory.Dispose();

    private HttpClient Designer() => _factory.CreateApiClient(_designerId, Roles.Designer);

    [Fact]
    public async Task Create_AsSales_ReturnsCreatedRequestedDesign()
    {
        var salesId = Guid.CreateVersion7();
        using var client = _factory.CreateApiClient(salesId, Roles.Sales);
        var request = CreateRequest();

        var response = await client.PostAsJsonAsync("/api/designs", request, Json);
        var design = await response.ReadAsync<FveDesignResponse>(HttpStatusCode.Created);

        Assert.Equal($"/api/designs/{design.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(FveDesignStatus.Requested, design.Status);
        Assert.Equal(request.CustomerId, design.CustomerId);
        Assert.Equal(request.SupplyPointId, design.SupplyPointId);
        Assert.Equal(salesId, design.CreatedBy);
    }

    [Fact]
    public async Task Create_WithoutSupplyPoint_Returns400()
    {
        using var client = Designer();

        var response = await client.PostAsJsonAsync("/api/designs", CreateRequest() with { SupplyPointId = null }, Json);
        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);

        Assert.Contains("SupplyPointId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_Anonymous_Returns401()
    {
        using var client = _factory.CreateApiClient();

        var response = await client.PostAsJsonAsync("/api/designs", CreateRequest(), Json);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.Technician)]
    [InlineData(Roles.Customer)]
    [InlineData(Roles.Operator)]
    public async Task Create_WithWrongRole_Returns403(string role)
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), role);

        var response = await client.PostAsJsonAsync("/api/designs", CreateRequest(), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FullFlow_SurveyAndParameters_PersistsRoofPlanesAndMovesToInDesign()
    {
        using var client = Designer();
        var created = await client.CreateDesignAsync();

        var surveyed = await client.SurveyAsync(created.Id);
        Assert.Equal(FveDesignStatus.SiteSurveyDone, surveyed.Status);
        Assert.Equal(_designerId, surveyed.SiteSurvey?.SurveyedBy);

        var east = SouthPlane(20) with { Name = "Východ", AzimuthDeg = -90, TiltDeg = 30, ShadingLossPercent = 10, RoofType = RoofType.PitchedMetal };
        var updated = await client.SetParametersAsync(created.Id, Parameters(SouthPlane(), east));

        Assert.Equal(FveDesignStatus.InDesign, updated.Status);
        var fetched = await client.GetFromJsonAsync<FveDesignResponse>($"/api/designs/{created.Id}", Json);
        Assert.NotNull(fetched);
        Assert.Equal(2, fetched.RoofPlanes.Count);
        Assert.Equal(-90, fetched.RoofPlanes[1].AzimuthDeg);
        Assert.Equal(RoofType.PitchedMetal, fetched.RoofPlanes[1].RoofType);
        Assert.Equal(10, fetched.RoofPlanes[1].ShadingLossPercent);
        Assert.Equal(4500, fetched.AnnualConsumptionKwh);
        Assert.Equal(ConsumptionProfile.Household, fetched.ConsumptionProfile);
        Assert.Equal(48.15, fetched.Latitude);
    }

    [Fact]
    public async Task UpdateParameters_Again_ReplacesRoofPlanes()
    {
        using var client = Designer();
        var design = await client.CreateInDesignAsync(Parameters(SouthPlane(), SouthPlane(10) with { Name = "Garáž" }));

        await client.SetParametersAsync(design.Id, Parameters(SouthPlane(50) with { Name = "Nová" }));

        var stored = await _factory.ExecuteDbAsync(db => db.Set<FveDesign>().AsNoTracking().SingleAsync(d => d.Id == design.Id));
        var plane = Assert.Single(stored.RoofPlanes);
        Assert.Equal("Nová", plane.Name);
        Assert.Equal(50, plane.UsableAreaM2);
    }

    [Fact]
    public async Task UpdateParameters_TiltOutOfRange_Returns400()
    {
        using var client = Designer();
        var design = await client.CreateDesignAsync();
        await client.SurveyAsync(design.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/designs/{design.Id}/parameters", Parameters(SouthPlane() with { TiltDeg = 95 }), Json);
        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);

        Assert.Contains("RoofPlanes[0].TiltDeg", problem.Errors.Keys);
    }

    [Fact]
    public async Task UpdateParameters_MissingOrientation_Returns400()
    {
        using var client = Designer();
        var design = await client.CreateDesignAsync();
        await client.SurveyAsync(design.Id);

        var response = await client.PutAsJsonAsync(
            $"/api/designs/{design.Id}/parameters", Parameters(SouthPlane() with { AzimuthDeg = null }), Json);
        var problem = await response.ReadAsync<ValidationProblemDetails>(HttpStatusCode.BadRequest);

        Assert.Contains("RoofPlanes[0].AzimuthDeg", problem.Errors.Keys);
    }

    [Fact]
    public async Task UpdateParameters_BeforeSiteSurvey_Returns409()
    {
        using var client = Designer();
        var design = await client.CreateDesignAsync();

        var response = await client.PutAsJsonAsync($"/api/designs/{design.Id}/parameters", Parameters(), Json);

        await response.ReadAsync<ProblemDetails>(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateParameters_AsSales_Returns403()
    {
        using var designer = Designer();
        var design = await designer.CreateDesignAsync();
        await designer.SurveyAsync(design.Id);
        using var sales = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Sales);

        var response = await sales.PutAsJsonAsync($"/api/designs/{design.Id}/parameters", Parameters(), Json);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MarkInfeasible_AfterSurvey_ReturnsInfeasibleAndBlocksParameters()
    {
        using var client = Designer();
        var design = await client.CreateDesignAsync();
        await client.SurveyAsync(design.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/designs/{design.Id}/mark-infeasible", new MarkInfeasibleRequest { Reason = "Severná strecha, silné tienenie" }, Json);
        var infeasible = await response.ReadAsync<FveDesignResponse>(HttpStatusCode.OK);

        Assert.Equal(FveDesignStatus.Infeasible, infeasible.Status);
        Assert.Equal("Severná strecha, silné tienenie", infeasible.InfeasibleReason);
        var blocked = await client.PutAsJsonAsync($"/api/designs/{design.Id}/parameters", Parameters(), Json);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
    }

    [Fact]
    public async Task MarkInfeasible_WithoutReason_Returns400()
    {
        using var client = Designer();
        var design = await client.CreateDesignAsync();

        var response = await client.PostAsJsonAsync($"/api/designs/{design.Id}/mark-infeasible", new MarkInfeasibleRequest(), Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SiteSurvey_Twice_Returns409()
    {
        using var client = Designer();
        var design = await client.CreateDesignAsync();
        await client.SurveyAsync(design.Id);

        var response = await client.PostAsJsonAsync($"/api/designs/{design.Id}/site-survey", new SiteSurveyRequest { Notes = "x" }, Json);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UnknownDesign_Returns404()
    {
        using var client = Designer();
        var id = Guid.CreateVersion7();

        var get = await client.GetAsync($"/api/designs/{id}");
        var survey = await client.PostAsJsonAsync($"/api/designs/{id}/site-survey", new SiteSurveyRequest { Notes = "x" }, Json);

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, survey.StatusCode);
    }

    [Fact]
    public async Task List_FiltersBySupplyPointAndStatus()
    {
        using var client = Designer();
        var first = await client.CreateDesignAsync();
        var second = await client.CreateDesignAsync();
        await client.SurveyAsync(second.Id);

        var bySupplyPoint = await client.GetFromJsonAsync<List<FveDesignResponse>>($"/api/designs?supplyPointId={first.SupplyPointId}", Json);
        var surveyed = await client.GetFromJsonAsync<List<FveDesignResponse>>("/api/designs?status=SiteSurveyDone", Json);

        Assert.Equal(first.Id, Assert.Single(bySupplyPoint!).Id);
        Assert.Equal(second.Id, Assert.Single(surveyed!).Id);
    }

    [Fact]
    public async Task List_AsCustomer_Returns403()
    {
        using var client = _factory.CreateApiClient(Guid.CreateVersion7(), Roles.Customer);

        var response = await client.GetAsync("/api/designs");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
