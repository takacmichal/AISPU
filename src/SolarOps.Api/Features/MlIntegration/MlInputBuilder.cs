using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Production;

namespace SolarOps.Api.Features.MlIntegration;

/// <summary>One stored forecast hour; <see cref="ForecastTime"/> is the raw Open-Meteo label, the END of the hour.</summary>
internal sealed record ForecastHour(
    DateTime ForecastTime,
    double? GhiWm2,
    double? DniWm2,
    double? DhiWm2,
    double? CloudCoverPct,
    double? TemperatureC,
    double? WindSpeedMs);

/// <summary>A stored prediction of one hour as the detection needs it.</summary>
internal sealed record HourlyExpectation(DateTimeOffset HourStart, double ExpectedPowerW, double? GhiWm2, double? CloudCoverPct);

/// <summary>
/// Turns stored data into the inputs of the ML service. The time labels differ and are the easy thing to get wrong:
/// Open-Meteo labels an hour by its end, predictions by its start (ADR #189), the detection by its end.
/// </summary>
internal static class MlInputBuilder
{
    /// <summary>Limit of one prediction request in the ML service.</summary>
    public const int MaxWeatherHours = 168;

    /// <summary>Limit of one detection request in the ML service (one quarter of hourly points).</summary>
    public const int MaxDetectionHours = 24 * 92;

    /// <summary>Telemetry arrives every 5 minutes (ADR #185), so a complete hour has 12 windows.</summary>
    private const double WindowsPerHour = 12;

    /// <summary>The first <see cref="MaxWeatherHours"/> usable forecast hours, in time order.</summary>
    public static IReadOnlyList<MlWeatherHour> WeatherHours(IEnumerable<ForecastHour> forecast) =>
        UsableWeatherHours(forecast).Take(MaxWeatherHours).ToList();

    /// <summary>All forecast hours the service can use, in time order (callers split them into requests of at most 168).</summary>
    public static IReadOnlyList<MlWeatherHour> UsableWeatherHours(IEnumerable<ForecastHour> forecast) =>
        forecast
            .Where(f => f is { TemperatureC: not null, GhiWm2: not null, DniWm2: not null, DhiWm2: not null })
            .OrderBy(f => f.ForecastTime)
            .Select(f => new MlWeatherHour(
                new DateTimeOffset(DateTime.SpecifyKind(f.ForecastTime, DateTimeKind.Utc)).AddHours(-1),
                f.GhiWm2!.Value,
                f.DniWm2!.Value,
                f.DhiWm2!.Value,
                f.CloudCoverPct,
                f.TemperatureC!.Value,
                f.WindSpeedMs))
            .ToList();

    /// <summary>The service models one plane, so the largest roof surface stands for the whole plant.</summary>
    public static RoofPlane? LargestPlane(IEnumerable<RoofPlane> planes) =>
        planes.OrderByDescending(p => p.UsableAreaM2).FirstOrDefault();

    /// <summary>
    /// One point per predicted hour. The mean power of an hour in W equals its energy in Wh. An hour without telemetry is
    /// sent with no actual power, which the service treats as missing data.
    /// </summary>
    public static IReadOnlyList<MlDetectionPoint> DetectionPoints(
        IEnumerable<HourlyExpectation> expectations,
        IEnumerable<ProductionPointResponse> actual)
    {
        var measured = actual.ToDictionary(p => p.PeriodStart.ToUniversalTime());
        return expectations
            .OrderBy(e => e.HourStart)
            .Select(e =>
            {
                var hasData = measured.TryGetValue(e.HourStart.ToUniversalTime(), out var point);
                return new MlDetectionPoint(
                    e.HourStart.AddHours(1),
                    hasData ? point!.EnergyKwh * 1000 : null,
                    e.ExpectedPowerW,
                    e.GhiWm2,
                    e.CloudCoverPct,
                    hasData ? Math.Min(1, point!.SampleCount / WindowsPerHour) : 0);
            })
            .ToList();
    }

    /// <summary>
    /// Start of the detection window. An ongoing alarm extends it back to the first hour of the alarm: the id of an alarm
    /// comes from its start, so a window cutting the event would report it again as a new alarm.
    /// </summary>
    public static DateTimeOffset DetectionWindowStart(DateTimeOffset to, int lookbackHours, DateTimeOffset? earliestOngoingStart)
    {
        var from = to.AddHours(-lookbackHours);
        if (earliestOngoingStart is { } start && start.AddHours(-1) < from)
        {
            from = start.AddHours(-1);
        }

        var earliestAllowed = to.AddHours(-MaxDetectionHours);
        return from < earliestAllowed ? earliestAllowed : from;
    }
}
