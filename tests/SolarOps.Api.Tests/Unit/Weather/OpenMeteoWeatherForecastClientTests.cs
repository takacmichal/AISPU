using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.Weather;

namespace SolarOps.Api.Tests.Unit.Weather;

public sealed class OpenMeteoWeatherForecastClientTests
{
    private static readonly DateTime Start = new(2026, 11, 3, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetForecastAsync_ParsesHourlyValuesInUtc()
    {
        var handler = new StubHandler(_ => Json(Response()));
        var client = CreateClient(handler);

        var points = await client.GetForecastAsync(48.72, 21.26, CancellationToken.None);

        Assert.Equal(48, points.Count);
        Assert.Equal(Start, points[0].ForecastTime);
        Assert.Equal(DateTimeKind.Utc, points[0].ForecastTime.Kind);
        Assert.Equal(0d, points[0].ShortwaveRadiationWm2);
        Assert.Equal(1d, points[0].DirectNormalIrradianceWm2);
        Assert.Equal(2d, points[0].DiffuseRadiationWm2);
        Assert.Equal(0d, points[0].CloudCoverPercent);
        Assert.Equal(10d, points[0].TemperatureC);
        Assert.Equal(1d, points[0].WindSpeedMs);
        Assert.Equal(47d, points[^1].ShortwaveRadiationWm2);
        Assert.Equal(Start.AddHours(47), points[^1].ForecastTime);
    }

    [Fact]
    public async Task GetForecastAsync_UsesConfiguredRequestAndOmitsEmptyApiKey()
    {
        var handler = new StubHandler(_ => Json(Response()));
        var client = CreateClient(handler, new WeatherOptions
        {
            BaseUrl = "https://weather.test/v1/forecast",
            ForecastHours = 48,
            ApiKey = "",
        });

        await client.GetForecastAsync(48.72, 21.26, CancellationToken.None);

        var uri = Assert.Single(handler.Requests);
        Assert.Equal("https://weather.test/v1/forecast", uri.GetLeftPart(UriPartial.Path));
        var query = QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("48.72", query["latitude"].ToString());
        Assert.Equal("21.26", query["longitude"].ToString());
        Assert.Equal("48", query["forecast_hours"].ToString());
        Assert.Equal("UTC", query["timezone"].ToString());
        Assert.Equal("ms", query["wind_speed_unit"].ToString());
        Assert.Equal(
            "shortwave_radiation,direct_normal_irradiance,diffuse_radiation,cloud_cover,temperature_2m,wind_speed_10m",
            query["hourly"].ToString());
        Assert.False(query.ContainsKey("apikey"));
    }

    [Fact]
    public async Task GetForecastAsync_AddsApiKeyFromConfiguration()
    {
        var handler = new StubHandler(_ => Json(Response()));
        var client = CreateClient(handler, new WeatherOptions { ApiKey = "paid-plan-key" });

        await client.GetForecastAsync(48.72, 21.26, CancellationToken.None);

        var query = QueryHelpers.ParseQuery(Assert.Single(handler.Requests).Query);
        Assert.Equal("paid-plan-key", query["apikey"].ToString());
    }

    [Fact]
    public async Task GetForecastAsync_ReturnsNullForMissingValues()
    {
        var handler = new StubHandler(_ => Json(Response(firstValueNull: true)));
        var client = CreateClient(handler);

        var points = await client.GetForecastAsync(48.72, 21.26, CancellationToken.None);

        Assert.Null(points[0].ShortwaveRadiationWm2);
        Assert.Equal(1d, points[0].DirectNormalIrradianceWm2);
    }

    [Fact]
    public async Task GetForecastAsync_ThrowsOnApiFailure()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("try later"),
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetForecastAsync(48.72, 21.26, CancellationToken.None));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        Assert.Contains("try later", exception.Message);
    }

    [Fact]
    public async Task GetForecastAsync_ThrowsOnMalformedResponse()
    {
        var handler = new StubHandler(_ => Json("""{"hourly":{"time":["2026-11-03T00:00"]}}"""));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetForecastAsync(48.72, 21.26, CancellationToken.None));
    }

    private static OpenMeteoWeatherForecastClient CreateClient(
        HttpMessageHandler handler,
        WeatherOptions? options = null) =>
        new(new HttpClient(handler), Options.Create(options ?? new WeatherOptions()));

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
    };

    private static string Response(bool firstValueNull = false)
    {
        var times = Enumerable.Range(0, 48).Select(i => Start.AddHours(i).ToString("yyyy-MM-dd'T'HH:mm")).ToArray();
        var shortwave = Enumerable.Range(0, 48).Select(i => i == 0 && firstValueNull ? (double?)null : i).ToArray();
        var directNormal = Enumerable.Range(1, 48).Select(i => (double?)i).ToArray();
        var diffuse = Enumerable.Range(2, 48).Select(i => (double?)i).ToArray();
        var cloudCover = Enumerable.Range(0, 48).Select(i => (double?)i).ToArray();
        var temperature = Enumerable.Range(10, 48).Select(i => (double?)i).ToArray();
        var windSpeed = Enumerable.Range(1, 48).Select(i => (double?)i).ToArray();

        return JsonSerializer.Serialize(new
        {
            hourly = new
            {
                time = times,
                shortwave_radiation = shortwave,
                direct_normal_irradiance = directNormal,
                diffuse_radiation = diffuse,
                cloud_cover = cloudCover,
                temperature_2m = temperature,
                wind_speed_10m = windSpeed,
            },
        });
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }
}
