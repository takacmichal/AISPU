using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Weather;

/// <summary>Outcome of one daily weather refresh run.</summary>
public sealed record WeatherForecastRefreshResult(
    int Installations,
    int Succeeded,
    int Skipped,
    int Failed,
    int RowsStored);

/// <summary>Fetches and stores the 48-hour forecast for every installation with known coordinates.</summary>
public interface IWeatherForecastRefreshService
{
    Task<WeatherForecastRefreshResult> RefreshAsync(CancellationToken ct);
}

/// <summary>
/// Refreshes the forecast of all installations (story #104). Coordinates come from the newest
/// <see cref="FveDesign"/> of the installation's supply point. API failures are logged and retried; a failed
/// installation keeps its previous forecast because rows are replaced only after a successful response.
/// </summary>
internal sealed class WeatherForecastRefreshService(
    AppDbContext db,
    IWeatherForecastClient client,
    TimeProvider timeProvider,
    IOptions<WeatherOptions> options,
    ILogger<WeatherForecastRefreshService> logger) : IWeatherForecastRefreshService
{
    public async Task<WeatherForecastRefreshResult> RefreshAsync(CancellationToken ct)
    {
        var settings = options.Value;
        var installations = await db.Set<Installation>()
            .AsNoTracking()
            .Select(i => new { i.Id, i.SupplyPointId })
            .ToListAsync(ct);

        if (installations.Count == 0)
        {
            return new WeatherForecastRefreshResult(0, 0, 0, 0, 0);
        }

        var supplyPointIds = installations.Select(i => i.SupplyPointId).Distinct().ToList();
        var designs = await db.Set<FveDesign>()
            .AsNoTracking()
            .Where(d => supplyPointIds.Contains(d.SupplyPointId) && d.Latitude != null && d.Longitude != null)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        // The newest design of each supply point carries the coordinates used for the forecast.
        var locationBySupplyPoint = designs
            .GroupBy(d => d.SupplyPointId)
            .ToDictionary(group => group.Key, group => group.First());

        var succeeded = 0;
        var skipped = 0;
        var failed = 0;
        var rowsStored = 0;

        foreach (var installation in installations)
        {
            if (!locationBySupplyPoint.TryGetValue(installation.SupplyPointId, out var design))
            {
                skipped++;
                logger.LogDebug(
                    "Skipping installation {InstallationId}: no design with coordinates for supply point {SupplyPointId}",
                    installation.Id,
                    installation.SupplyPointId);
                continue;
            }

            var latitude = design.Latitude!.Value;
            var longitude = design.Longitude!.Value;
            var points = await FetchWithRetryAsync(installation.Id, latitude, longitude, settings, ct);
            if (points is null)
            {
                failed++;
                continue;
            }

            if (points.Count == 0)
            {
                logger.LogError(
                    "Weather forecast for installation {InstallationId} was empty; keeping the previous forecast",
                    installation.Id);
                failed++;
                continue;
            }

            // The provider is asked for ForecastHours; trim defensively so a longer response cannot grow the table.
            var forecast = points.Count > settings.ForecastHours
                ? points.Take(settings.ForecastHours).ToList()
                : points;

            try
            {
                await ReplaceAsync(installation.Id, latitude, longitude, forecast, ct);
                succeeded++;
                rowsStored += forecast.Count;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Storing the weather forecast for installation {InstallationId} failed", installation.Id);
                failed++;
            }
        }

        return new WeatherForecastRefreshResult(installations.Count, succeeded, skipped, failed, rowsStored);
    }

    private async Task<IReadOnlyList<WeatherForecastPoint>?> FetchWithRetryAsync(
        Guid installationId,
        double latitude,
        double longitude,
        WeatherOptions settings,
        CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, settings.MaxAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await client.GetForecastAsync(latitude, longitude, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    logger.LogError(
                        ex,
                        "Weather forecast fetch failed for installation {InstallationId} after {Attempts} attempt(s)",
                        installationId,
                        maxAttempts);
                    return null;
                }

                logger.LogWarning(
                    ex,
                    "Weather forecast fetch failed for installation {InstallationId} (attempt {Attempt}/{MaxAttempts}); retrying in {DelaySeconds}s",
                    installationId,
                    attempt,
                    maxAttempts,
                    settings.RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.RetryDelaySeconds)), ct);
            }
        }

        return null;
    }

    private async Task ReplaceAsync(
        Guid installationId,
        double latitude,
        double longitude,
        IReadOnlyList<WeatherForecastPoint> points,
        CancellationToken ct)
    {
        var fetchedAt = timeProvider.GetUtcNow().UtcDateTime;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Set<WeatherForecast>()
            .Where(f => f.InstallationId == installationId)
            .ExecuteDeleteAsync(ct);
        // ExecuteDelete bypasses the change tracker; detach any rows tracked by a previous run in this scope.
        db.ChangeTracker.Clear();
        db.AddRange(points.Select(point => WeatherForecast.Create(installationId, point, latitude, longitude, fetchedAt)));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
