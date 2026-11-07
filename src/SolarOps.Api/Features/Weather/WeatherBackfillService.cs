using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Weather;

/// <summary>Outcome of the backfill for one installation.</summary>
/// <param name="InstallationId">Installation of the item.</param>
/// <param name="Status">Updated: hours stored; Skipped: no coordinates or no complete hour in the archive; Failed: the API failed.</param>
/// <param name="RowsStored">Hours stored for the installation.</param>
/// <param name="SkippedHours">Hours of the period left out because the archive had no complete values for them.</param>
public sealed record WeatherBackfillItem(Guid InstallationId, WeatherBackfillStatus Status, int RowsStored, int SkippedHours);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WeatherBackfillStatus
{
    Updated,
    Skipped,
    Failed,
}

/// <summary>Outcome of one backfill run; the totals follow the log line of the daily refresh.</summary>
public sealed record WeatherBackfillResult(
    DateOnly From,
    DateOnly To,
    int Installations,
    int Succeeded,
    int Skipped,
    int Failed,
    int RowsStored,
    IReadOnlyList<WeatherBackfillItem> Items);

/// <summary>Fetches and stores past hourly weather from the Open-Meteo archive (manual, admin only).</summary>
public interface IWeatherBackfillService
{
    Task<WeatherBackfillResult> BackfillAsync(HistoricalPeriod period, CancellationToken ct);
}

/// <summary>
/// Stores archive observations as <see cref="WeatherForecast"/> rows, so past hours can be predicted like forecast hours.
/// Idempotent: the rows of the fetched hours are deleted and inserted again in one transaction, so a rerun changes nothing.
/// Hours with a missing temperature or irradiance are not stored (they would replace valid rows with nulls). A failed installation
/// keeps its existing rows. Rows of other hours are never touched.
/// </summary>
internal sealed class WeatherBackfillService(
    AppDbContext db,
    IWeatherArchiveClient archive,
    TimeProvider timeProvider,
    IOptions<WeatherOptions> options,
    ILogger<WeatherBackfillService> logger) : IWeatherBackfillService
{
    public async Task<WeatherBackfillResult> BackfillAsync(HistoricalPeriod period, CancellationToken ct)
    {
        var from = period.From!.Value;
        var to = period.To!.Value;
        var settings = options.Value;

        var installations = await db.Set<Installation>()
            .AsNoTracking()
            .Select(i => new { i.Id, i.SupplyPointId })
            .ToListAsync(ct);

        var supplyPointIds = installations.Select(i => i.SupplyPointId).Distinct().ToList();
        var designs = await db.Set<FveDesign>()
            .AsNoTracking()
            .Where(d => supplyPointIds.Contains(d.SupplyPointId) && d.Latitude != null && d.Longitude != null)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

        // Same location rule as the forecast refresh: the newest design of the supply point with coordinates.
        var locationBySupplyPoint = designs
            .GroupBy(d => d.SupplyPointId)
            .ToDictionary(group => group.Key, group => group.First());

        // Open-Meteo labels an hour by its end (ADR #189), so the days from..to are the labels (from 00:00, to+1 00:00].
        // The archive is asked for one more day to get the label of the last hour.
        var windowStart = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var windowEnd = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var fetchedAt = timeProvider.GetUtcNow().UtcDateTime;

        var items = new List<WeatherBackfillItem>(installations.Count);
        foreach (var installation in installations)
        {
            if (!locationBySupplyPoint.TryGetValue(installation.SupplyPointId, out var design))
            {
                items.Add(new WeatherBackfillItem(installation.Id, WeatherBackfillStatus.Skipped, 0, 0));
                continue;
            }

            var latitude = design.Latitude!.Value;
            var longitude = design.Longitude!.Value;
            var points = await FetchWithRetryAsync(installation.Id, latitude, longitude, from, to.AddDays(1), settings, ct);
            if (points is null)
            {
                items.Add(new WeatherBackfillItem(installation.Id, WeatherBackfillStatus.Failed, 0, 0));
                continue;
            }

            var inPeriod = points.Where(p => p.ForecastTime > windowStart && p.ForecastTime <= windowEnd).ToList();
            var usable = inPeriod
                .Where(p => p.TemperatureC is not null && p.ShortwaveRadiationWm2 is not null
                    && p.DirectNormalIrradianceWm2 is not null && p.DiffuseRadiationWm2 is not null)
                .ToList();
            var skippedHours = inPeriod.Count - usable.Count;
            if (usable.Count == 0)
            {
                logger.LogWarning(
                    "Weather archive for installation {InstallationId} has no complete hour in {From}..{To}; nothing stored",
                    installation.Id, from, to);
                items.Add(new WeatherBackfillItem(installation.Id, WeatherBackfillStatus.Skipped, 0, skippedHours));
                continue;
            }

            try
            {
                await ReplaceHoursAsync(installation.Id, latitude, longitude, usable, fetchedAt, ct);
                items.Add(new WeatherBackfillItem(installation.Id, WeatherBackfillStatus.Updated, usable.Count, skippedHours));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Storing the weather archive for installation {InstallationId} failed", installation.Id);
                items.Add(new WeatherBackfillItem(installation.Id, WeatherBackfillStatus.Failed, 0, skippedHours));
            }
        }

        var result = new WeatherBackfillResult(
            from,
            to,
            installations.Count,
            items.Count(i => i.Status == WeatherBackfillStatus.Updated),
            items.Count(i => i.Status == WeatherBackfillStatus.Skipped),
            items.Count(i => i.Status == WeatherBackfillStatus.Failed),
            items.Sum(i => i.RowsStored),
            items);

        logger.LogInformation(
            "Weather backfill {From}..{To} finished: {Succeeded}/{Installations} installations updated, "
            + "{Skipped} skipped, {Failed} failed, {RowsStored} rows stored",
            from,
            to,
            result.Succeeded,
            result.Installations,
            result.Skipped,
            result.Failed,
            result.RowsStored);

        return result;
    }

    private async Task<IReadOnlyList<WeatherForecastPoint>?> FetchWithRetryAsync(
        Guid installationId,
        double latitude,
        double longitude,
        DateOnly from,
        DateOnly to,
        WeatherOptions settings,
        CancellationToken ct)
    {
        var maxAttempts = Math.Max(1, settings.MaxAttempts);
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await archive.GetHistoricalAsync(latitude, longitude, from, to, ct);
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
                        "Weather archive fetch failed for installation {InstallationId} after {Attempts} attempt(s)",
                        installationId,
                        maxAttempts);
                    return null;
                }

                logger.LogWarning(
                    ex,
                    "Weather archive fetch failed for installation {InstallationId} (attempt {Attempt}/{MaxAttempts}); retrying in {DelaySeconds}s",
                    installationId,
                    attempt,
                    maxAttempts,
                    settings.RetryDelaySeconds);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, settings.RetryDelaySeconds)), ct);
            }
        }

        return null;
    }

    private async Task ReplaceHoursAsync(
        Guid installationId,
        double latitude,
        double longitude,
        IReadOnlyList<WeatherForecastPoint> points,
        DateTime fetchedAt,
        CancellationToken ct)
    {
        var hours = points.Select(p => p.ForecastTime).ToList();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Set<WeatherForecast>()
            .Where(f => f.InstallationId == installationId && hours.Contains(f.ForecastTime))
            .ExecuteDeleteAsync(ct);
        // ExecuteDelete bypasses the change tracker; detach any rows tracked earlier in this scope.
        db.ChangeTracker.Clear();
        db.AddRange(points.Select(point => WeatherForecast.Create(installationId, point, latitude, longitude, fetchedAt)));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
