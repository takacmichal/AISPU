using SolarOps.Api.Infrastructure.Jobs;

namespace SolarOps.Api.Features.Weather;

/// <summary>
/// One weather forecast refresh for a cron Container Apps Job (ADR #277). Ignores <see cref="WeatherOptions.Enabled"/>,
/// which only turns off the in-process schedule of the API.
/// </summary>
internal sealed class WeatherRefreshJob(IWeatherForecastRefreshService refresh, ILogger<WeatherRefreshJob> logger) : IJob
{
    public string Name => "weather-refresh";

    public async Task<bool> RunAsync(CancellationToken cancellationToken)
    {
        var result = await refresh.RefreshAsync(cancellationToken);
        logger.LogInformation(
            "Weather forecast refresh finished: {Succeeded}/{Installations} installations updated, {Skipped} skipped, {Failed} failed, {RowsStored} rows stored",
            result.Succeeded,
            result.Installations,
            result.Skipped,
            result.Failed,
            result.RowsStored);
        return result.Failed == 0;
    }
}
