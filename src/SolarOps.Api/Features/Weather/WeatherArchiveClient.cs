using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace SolarOps.Api.Features.Weather;

/// <summary>Provider-independent source of hourly observations (reanalysis) of past days for a location.</summary>
public interface IWeatherArchiveClient
{
    /// <summary>Hourly values from <paramref name="from"/> through <paramref name="to"/> (inclusive days, UTC).</summary>
    Task<IReadOnlyList<WeatherForecastPoint>> GetHistoricalAsync(
        double latitude,
        double longitude,
        DateOnly from,
        DateOnly to,
        CancellationToken ct);
}

/// <summary>
/// Open-Meteo Archive API client (ADR-180, ERA5). Same variables, units and UTC labels as the forecast client, so the
/// stored rows are interchangeable. The archive lags several days behind the present; recent hours may come back null.
/// </summary>
internal sealed class OpenMeteoWeatherArchiveClient(HttpClient http, IOptions<WeatherOptions> options) : IWeatherArchiveClient
{
    public async Task<IReadOnlyList<WeatherForecastPoint>> GetHistoricalAsync(
        double latitude,
        double longitude,
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var settings = options.Value;
        var query = new Dictionary<string, string?>
        {
            ["latitude"] = latitude.ToString("0.#####", CultureInfo.InvariantCulture),
            ["longitude"] = longitude.ToString("0.#####", CultureInfo.InvariantCulture),
            ["hourly"] = OpenMeteoWeatherForecastClient.HourlyVariables,
            ["start_date"] = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["end_date"] = to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["timezone"] = "UTC",
            ["wind_speed_unit"] = "ms",
        };

        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            query["apikey"] = settings.ApiKey;
        }

        return await OpenMeteoWeatherForecastClient.ExecuteAsync(
            http,
            QueryHelpers.AddQueryString(settings.ArchiveBaseUrl, query),
            ct);
    }
}
