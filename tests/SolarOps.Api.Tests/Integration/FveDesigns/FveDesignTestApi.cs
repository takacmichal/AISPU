using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SolarOps.Api.Features.FveDesigns;

namespace SolarOps.Api.Tests.Integration.FveDesigns;

/// <summary>Shared request builders and HTTP helpers for the FveDesigns integration tests.</summary>
internal static class FveDesignTestApi
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static CreateFveDesignRequest CreateRequest() => new()
    {
        CustomerId = Guid.CreateVersion7(),
        SupplyPointId = Guid.CreateVersion7(),
        Notes = "Rodinný dom, záujem o 5 kWp",
    };

    public static RoofPlaneRequest SouthPlane(double area = 30) => new()
    {
        Name = "Juh",
        AzimuthDeg = 0,
        TiltDeg = 35,
        UsableAreaM2 = area,
        ShadingLossPercent = 0,
        RoofType = RoofType.PitchedTile,
    };

    public static UpdateDesignParametersRequest Parameters(params RoofPlaneRequest[] planes) => new()
    {
        Latitude = 48.15,
        Longitude = 17.11,
        AnnualConsumptionKwh = 4500,
        ConsumptionProfile = ConsumptionProfile.Household,
        RoofPlanes = planes.Length == 0 ? [SouthPlane()] : [.. planes],
    };

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"Expected {expected}, got {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, Json)!;
    }

    public static async Task<FveDesignResponse> CreateDesignAsync(this HttpClient client) =>
        await (await client.PostAsJsonAsync("/api/designs", CreateRequest(), Json)).ReadAsync<FveDesignResponse>(HttpStatusCode.Created);

    public static async Task<FveDesignResponse> SurveyAsync(this HttpClient client, Guid id) =>
        await (await client.PostAsJsonAsync($"/api/designs/{id}/site-survey", new SiteSurveyRequest { Notes = "Strecha v poriadku" }, Json))
            .ReadAsync<FveDesignResponse>(HttpStatusCode.OK);

    public static async Task<FveDesignResponse> SetParametersAsync(this HttpClient client, Guid id, UpdateDesignParametersRequest? request = null) =>
        await (await client.PutAsJsonAsync($"/api/designs/{id}/parameters", request ?? Parameters(), Json))
            .ReadAsync<FveDesignResponse>(HttpStatusCode.OK);

    /// <summary>Creates a design and moves it to <see cref="FveDesignStatus.InDesign"/>.</summary>
    public static async Task<FveDesignResponse> CreateInDesignAsync(this HttpClient client, UpdateDesignParametersRequest? request = null)
    {
        var design = await client.CreateDesignAsync();
        await client.SurveyAsync(design.Id);
        return await client.SetParametersAsync(design.Id, request);
    }
}
