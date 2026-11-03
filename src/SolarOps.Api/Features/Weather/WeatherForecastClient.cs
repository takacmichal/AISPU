using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace SolarOps.Api.Features.Weather;

/// <summary>One hourly value returned by a forecast provider.</summary>
public sealed record WeatherForecastPoint(
    DateTime ForecastTime,
    double? ShortwaveRadiationWm2,
    double? DirectNormalIrradianceWm2,
    double? DiffuseRadiationWm2,
    double? CloudCoverPercent,
    double? TemperatureC,
    double? WindSpeedMs);

/// <summary>Provider-independent source of an hourly weather forecast for a location.</summary>
public interface IWeatherForecastClient
{
    Task<IReadOnlyList<WeatherForecastPoint>> GetForecastAsync(double latitude, double longitude, CancellationToken ct);
}

/// <summary>
/// Open-Meteo Forecast API client (ADR-180). Requests hourly GHI/DNI/DHI, cloud cover, temperature and wind in
/// UTC and SI units. The optional paid-plan key is appended only when configured.
/// </summary>
internal sealed class OpenMeteoWeatherForecastClient(HttpClient http, IOptions<WeatherOptions> options) : IWeatherForecastClient
{
    private const string HourlyVariables =
        "shortwave_radiation,direct_normal_irradiance,diffuse_radiation,cloud_cover,temperature_2m,wind_speed_10m";

    public async Task<IReadOnlyList<WeatherForecastPoint>> GetForecastAsync(
        double latitude,
        double longitude,
        CancellationToken ct)
    {
        var settings = options.Value;
        var query = new Dictionary<string, string?>
        {
            ["latitude"] = latitude.ToString("0.#####", CultureInfo.InvariantCulture),
            ["longitude"] = longitude.ToString("0.#####", CultureInfo.InvariantCulture),
            ["hourly"] = HourlyVariables,
            ["forecast_hours"] = settings.ForecastHours.ToString(CultureInfo.InvariantCulture),
            ["timezone"] = "UTC",
            ["wind_speed_unit"] = "ms",
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            query["apikey"] = settings.ApiKey;
        }

        var url = QueryHelpers.AddQueryString(settings.BaseUrl, query);
        using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException(
                $"Open-Meteo returned {(int)response.StatusCode} ({response.ReasonPhrase}): {body}",
                null,
                response.StatusCode);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        return Parse(document.RootElement);
    }

    /// <summary>Parses the <c>hourly</c> block of an Open-Meteo response; exposed for unit tests.</summary>
    internal static IReadOnlyList<WeatherForecastPoint> Parse(JsonElement root)
    {
        if (!root.TryGetProperty("hourly", out var hourly) || hourly.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Open-Meteo response does not contain an hourly block.");
        }

        var times = ReadArray(hourly, "time");
        var shortwave = ReadArray(hourly, "shortwave_radiation");
        var directNormal = ReadArray(hourly, "direct_normal_irradiance");
        var diffuse = ReadArray(hourly, "diffuse_radiation");
        var cloudCover = ReadArray(hourly, "cloud_cover");
        var temperature = ReadArray(hourly, "temperature_2m");
        var windSpeed = ReadArray(hourly, "wind_speed_10m");

        var length = times.GetArrayLength();
        if (new[] { shortwave, directNormal, diffuse, cloudCover, temperature, windSpeed }
            .Any(array => array.GetArrayLength() != length))
        {
            throw new InvalidOperationException("Open-Meteo hourly arrays have different lengths.");
        }

        var points = new List<WeatherForecastPoint>(length);
        for (var i = 0; i < length; i++)
        {
            points.Add(new WeatherForecastPoint(
                ParseUtcTime(times[i]),
                ReadNumber(shortwave, i),
                ReadNumber(directNormal, i),
                ReadNumber(diffuse, i),
                ReadNumber(cloudCover, i),
                ReadNumber(temperature, i),
                ReadNumber(windSpeed, i)));
        }

        return points;
    }

    private static JsonElement ReadArray(JsonElement hourly, string name)
    {
        if (!hourly.TryGetProperty(name, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Open-Meteo response does not contain the '{name}' array.");
        }

        return array;
    }

    private static double? ReadNumber(JsonElement array, int index) => array[index].ValueKind switch
    {
        JsonValueKind.Number => array[index].GetDouble(),
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException($"Open-Meteo returned a non-numeric value at index {index}."),
    };

    private static DateTime ParseUtcTime(JsonElement element)
    {
        var value = element.ValueKind == JsonValueKind.String ? element.GetString() : null;
        if (!DateTime.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            throw new InvalidOperationException($"Open-Meteo returned an invalid time '{value}'.");
        }

        return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
    }
}
