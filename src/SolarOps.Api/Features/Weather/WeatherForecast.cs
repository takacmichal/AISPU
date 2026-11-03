namespace SolarOps.Api.Features.Weather;

/// <summary>
/// One hourly point of an installation's weather forecast (ADR-180, story #104). Timestamps are UTC and the
/// radiation values are in W/m², cloud cover in %, temperature in °C and wind speed in m/s.
/// The installation is referenced by id only (module boundary); rows of an installation are replaced as a
/// whole after a successful API response.
/// </summary>
public sealed class WeatherForecast
{
    private WeatherForecast()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary>Forecast hour, UTC.</summary>
    public DateTime ForecastTime { get; private set; }

    /// <summary>Global horizontal irradiance (GHI), W/m².</summary>
    public double? ShortwaveRadiationWm2 { get; private set; }

    /// <summary>Direct normal irradiance (DNI), W/m².</summary>
    public double? DirectNormalIrradianceWm2 { get; private set; }

    /// <summary>Diffuse horizontal irradiance (DHI), W/m².</summary>
    public double? DiffuseRadiationWm2 { get; private set; }

    public double? CloudCoverPercent { get; private set; }

    public double? TemperatureC { get; private set; }

    public double? WindSpeedMs { get; private set; }

    /// <summary>Coordinates actually sent to the API (the provider may snap them to its grid).</summary>
    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    /// <summary>When the row was fetched, UTC.</summary>
    public DateTime FetchedAt { get; private set; }

    public static WeatherForecast Create(
        Guid installationId,
        WeatherForecastPoint point,
        double latitude,
        double longitude,
        DateTime fetchedAt) => new()
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installationId,
            ForecastTime = point.ForecastTime,
            ShortwaveRadiationWm2 = point.ShortwaveRadiationWm2,
            DirectNormalIrradianceWm2 = point.DirectNormalIrradianceWm2,
            DiffuseRadiationWm2 = point.DiffuseRadiationWm2,
            CloudCoverPercent = point.CloudCoverPercent,
            TemperatureC = point.TemperatureC,
            WindSpeedMs = point.WindSpeedMs,
            Latitude = latitude,
            Longitude = longitude,
            FetchedAt = fetchedAt,
        };
}
