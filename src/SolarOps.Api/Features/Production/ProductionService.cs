using Microsoft.EntityFrameworkCore;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Telemetry;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.Production;

public interface IProductionService
{
    Task<ProductionResponse> GetAsync(Guid installationId, ProductionPeriod period, CancellationToken ct);

    /// <summary>Production summed over the inverters of all installations the caller may see.</summary>
    Task<ProductionSummaryResponse> GetSummaryAsync(ProductionPeriod period, CancellationToken ct);

    Task<ProductionComparisonResponse> CompareAsync(Guid installationId, ProductionPeriod period, CancellationToken ct);

    /// <summary>
    /// Checks access (404 for a missing or foreign installation) and returns a lazy sequence of buckets. The query runs
    /// when the sequence is enumerated, so the export can stream it.
    /// </summary>
    Task<IAsyncEnumerable<ProductionBucket>> OpenExportAsync(Guid installationId, ProductionPeriod period, CancellationToken ct);
}

/// <summary>
/// Production read model over <c>telemetry_readings</c> (#100 to #102). All aggregation runs in PostgreSQL; the query
/// filters by <c>device_id = ANY(...)</c> and a <c>ts</c> range, which is a range scan of the primary key
/// <c>(device_id, ts)</c>, so no new index is needed.
/// </summary>
internal sealed class ProductionService(AppDbContext db, ICustomerScope customerScope, IProductionPredictionSource predictions)
    : IProductionService
{
    /// <summary>Readings this far before <c>from</c> are read only as the counter base of the first window.</summary>
    private static readonly TimeSpan Lookback = TimeSpan.FromSeconds(TelemetryBatchRequest.MaxIntervalSec);

    public async Task<ProductionResponse> GetAsync(Guid installationId, ProductionPeriod period, CancellationToken ct)
    {
        var buckets = await QueryAsync(installationId, period, ct);

        return new ProductionResponse(
            installationId,
            period.From,
            period.To,
            period.Granularity,
            period.TimeZoneId,
            ProductionCsv.Kilo(buckets.Sum(b => b.EnergyWh)),
            buckets.Count == 0 ? null : ProductionCsv.Kilo(buckets.Max(b => b.PeakPowerW)),
            buckets.Select(ToPoint).ToList());
    }

    public async Task<ProductionSummaryResponse> GetSummaryAsync(ProductionPeriod period, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var installations = db.Set<Installation>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            installations = installations.Where(x => x.CustomerId == own);
        }

        var installationCount = await installations.CountAsync(ct);
        var devices = installationCount == 0
            ? []
            : await db.Set<InstallationDevice>()
                .AsNoTracking()
                .Where(d => d.Type == DeviceType.Inverter && installations.Any(i => i.Id == d.InstallationId))
                .Select(d => d.Id)
                .ToArrayAsync(ct);

        // Buckets() groups the 5-minute windows by window_start over all given devices, so with the inverters of every
        // visible installation each window holds the power and energy of the whole fleet, and the bucket peak is the
        // highest whole-fleet power (not a sum of per-installation peaks).
        var buckets = devices.Length == 0 ? [] : await Buckets(devices, period).ToListAsync(ct);

        return new ProductionSummaryResponse(
            period.From,
            period.To,
            period.Granularity,
            period.TimeZoneId,
            installationCount,
            ProductionCsv.Kilo(buckets.Sum(b => b.EnergyWh)),
            buckets.Count == 0 ? null : ProductionCsv.Kilo(buckets.Max(b => b.PeakPowerW)),
            buckets.Select(ToPoint).ToList());
    }

    public async Task<ProductionComparisonResponse> CompareAsync(Guid installationId, ProductionPeriod period, CancellationToken ct)
    {
        var buckets = await QueryAsync(installationId, period, ct);
        var prediction = await predictions.GetAsync(installationId, period.From, period.To, ct);
        var points = ProductionComparison.Pair(buckets, prediction, period);
        var predictedPoints = points.Where(p => p.PredictedEnergyKwh is not null).ToList();

        return new ProductionComparisonResponse(
            installationId,
            period.From,
            period.To,
            period.Granularity,
            period.TimeZoneId,
            prediction.IsAvailable,
            prediction.ModelVersion,
            ProductionCsv.Kilo(buckets.Sum(b => b.EnergyWh)),
            predictedPoints.Count == 0 ? null : Math.Round(predictedPoints.Sum(p => p.PredictedEnergyKwh!.Value), 3),
            points);
    }

    public async Task<IAsyncEnumerable<ProductionBucket>> OpenExportAsync(Guid installationId, ProductionPeriod period, CancellationToken ct)
    {
        var devices = await InverterIdsAsync(installationId, ct);
        return devices.Length == 0 ? EmptyAsync() : Buckets(devices, period).AsAsyncEnumerable();
    }

    private async Task<List<ProductionBucket>> QueryAsync(Guid installationId, ProductionPeriod period, CancellationToken ct)
    {
        var devices = await InverterIdsAsync(installationId, ct);
        return devices.Length == 0 ? [] : await Buckets(devices, period).ToListAsync(ct);
    }

    /// <summary>
    /// Inverters of a visible installation, replaced ones included (their readings before the swap still count).
    /// A foreign installation of a customer is reported exactly like a missing one (404).
    /// </summary>
    private async Task<Guid[]> InverterIdsAsync(Guid installationId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visible = db.Set<Installation>().AsNoTracking().Where(x => x.Id == installationId);
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            visible = visible.Where(x => x.CustomerId == own);
        }

        if (!await visible.AnyAsync(ct))
        {
            throw new NotFoundException("Installation", installationId);
        }

        return await db.Set<InstallationDevice>()
            .AsNoTracking()
            .Where(d => d.InstallationId == installationId && d.Type == DeviceType.Inverter)
            .Select(d => d.Id)
            .ToArrayAsync(ct);
    }

    /// <summary>
    /// Aggregation in SQL:
    /// <list type="number">
    /// <item>a reading covers the window <c>(ts - interval_sec, ts]</c>, so it belongs to the bucket of the window start;</item>
    /// <item>energy of a window is the increase of the device's monotonic <c>total_wh</c> counter since its previous
    /// reading (a reset or replacement gives 0, never a negative value); one lookback hour supplies the base of the first window;</item>
    /// <item>windows are first summed over the inverters (installation power), then grouped into buckets, so the peak is
    /// the highest installation power, not a sum of per-inverter peaks;</item>
    /// <item>day and month buckets follow the local calendar of <c>timeZone</c> (<c>date_trunc(unit, ts, zone)</c>).</item>
    /// </list>
    /// Only buckets with data are returned, ordered by time.
    /// </summary>
    private IQueryable<ProductionBucket> Buckets(Guid[] deviceIds, ProductionPeriod period)
    {
        var from = period.From.UtcDateTime;
        var to = period.To.UtcDateTime;
        var readFrom = from - Lookback;
        var readTo = to + Lookback;
        var fiveMinutes = period.Granularity == ProductionGranularity.FiveMinutes;
        var unit = period.Granularity switch
        {
            ProductionGranularity.Day => "day",
            ProductionGranularity.Month => "month",
            _ => "hour",
        };
        // Hours are UTC hours (the same in every zone with whole-hour offsets); days and months are local.
        var bucketZone = period.Granularity is ProductionGranularity.Day or ProductionGranularity.Month ? period.TimeZoneId : "UTC";

        FormattableString sql = $"""
            WITH readings AS (
                SELECT r.ts - make_interval(secs => r.interval_sec) AS window_start,
                       GREATEST(r.ac_power_w, 0)::double precision AS power_w,
                       GREATEST(r.total_wh - LAG(r.total_wh) OVER (PARTITION BY r.device_id ORDER BY r.ts), 0) AS energy_wh
                FROM telemetry_readings r
                WHERE r.device_id = ANY({deviceIds}) AND r.ts >= {readFrom} AND r.ts <= {readTo}
            ), windows AS (
                SELECT window_start, SUM(power_w) AS power_w, COALESCE(SUM(energy_wh), 0) AS energy_wh
                FROM readings
                WHERE window_start >= {from} AND window_start < {to}
                GROUP BY window_start
            )
            SELECT CASE WHEN {fiveMinutes}
                        THEN date_bin(INTERVAL '5 minutes', window_start, TIMESTAMPTZ '2000-01-01 00:00:00+00')
                        ELSE date_trunc({unit}, window_start, {bucketZone})
                   END AS period_start,
                   SUM(energy_wh) AS energy_wh,
                   MAX(power_w) AS peak_power_w,
                   COUNT(*)::integer AS sample_count
            FROM windows
            GROUP BY 1
            ORDER BY 1
            """;

        return db.Database.SqlQuery<ProductionBucket>(sql);
    }

    private static ProductionPointResponse ToPoint(ProductionBucket bucket) => new(
        Utc(bucket.PeriodStart), ProductionCsv.Kilo(bucket.EnergyWh), ProductionCsv.Kilo(bucket.PeakPowerW), bucket.SampleCount);

    private static DateTimeOffset Utc(DateTime value) => new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static async IAsyncEnumerable<ProductionBucket> EmptyAsync()
    {
        await Task.CompletedTask;
        yield break;
    }
}
