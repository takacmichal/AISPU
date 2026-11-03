using Microsoft.Extensions.Options;

namespace SolarOps.Api.Features.Weather;

/// <summary>
/// Runs the daily weather refresh (#104): once at startup (configurable) and then every
/// <see cref="WeatherOptions.RefreshIntervalHours"/> hours.
/// </summary>
internal sealed class WeatherForecastBackgroundService(
    IServiceScopeFactory scopeFactory,
    IOptions<WeatherOptions> options,
    ILogger<WeatherForecastBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled)
        {
            logger.LogInformation("Weather forecast refresh is disabled (Weather:Enabled=false).");
            return;
        }

        if (settings.RunOnStartup)
        {
            await RunOnceAsync(stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromHours(Math.Max(1, settings.RefreshIntervalHours)));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider
                .GetRequiredService<IWeatherForecastRefreshService>()
                .RefreshAsync(ct);
            logger.LogInformation(
                "Weather forecast refresh finished: {Succeeded}/{Installations} installations updated, "
                + "{Skipped} skipped, {Failed} failed, {RowsStored} rows stored",
                result.Succeeded,
                result.Installations,
                result.Skipped,
                result.Failed,
                result.RowsStored);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Weather forecast refresh run failed");
        }
    }
}
