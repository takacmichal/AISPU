using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Weather;

/// <summary>
/// Weather forecast settings (<c>Weather</c> section). The API key is optional for the Open-Meteo free tier;
/// when a paid plan is used it comes from configuration or the <c>Weather__ApiKey</c> environment variable,
/// never from code.
/// </summary>
public sealed class WeatherOptions
{
    public const string SectionName = "Weather";

    /// <summary>Enables the daily background refresh. Tests set this to <c>false</c>.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Forecast API endpoint (ADR-180: Open-Meteo Forecast API).</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://api.open-meteo.com/v1/forecast";

    /// <summary>Historical (archive) API endpoint used by the backfill of past hours (ADR-180: ERA5 archive).</summary>
    [Required]
    public string ArchiveBaseUrl { get; set; } = "https://archive-api.open-meteo.com/v1/archive";

    /// <summary>Optional paid-plan API key; empty for the free tier.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Length of the stored forecast in hours (#104: 48).</summary>
    [Range(1, 384)]
    public int ForecastHours { get; set; } = 48;

    /// <summary>How often the background service refreshes the forecasts.</summary>
    [Range(1, 168)]
    public int RefreshIntervalHours { get; set; } = 24;

    /// <summary>Run one refresh immediately when the API starts, then every <see cref="RefreshIntervalHours"/>.</summary>
    public bool RunOnStartup { get; set; } = true;

    /// <summary>Number of attempts per installation when the API fails (1 = no retry).</summary>
    [Range(1, 10)]
    public int MaxAttempts { get; set; } = 3;

    /// <summary>Delay between failed attempts.</summary>
    [Range(0, 300)]
    public int RetryDelaySeconds { get; set; } = 5;

    /// <summary>HTTP timeout of a single forecast request.</summary>
    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}
