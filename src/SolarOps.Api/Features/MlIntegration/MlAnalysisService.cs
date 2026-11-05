using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Features.Production;
using SolarOps.Api.Features.Weather;
using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Persistence;

namespace SolarOps.Api.Features.MlIntegration;

public interface IMlAnalysisService
{
    /// <summary>Predicts from the stored forecast, stores the result, runs the detection and stores its alarms.</summary>
    Task<MlAnalysisResponse> RunAsync(Guid installationId, CancellationToken ct);

    /// <summary>Stored alarms of a visible installation, newest first; 404 for a missing or foreign one.</summary>
    Task<UnderproductionAlarmListResponse> ListAlarmsAsync(Guid installationId, CancellationToken ct);

    /// <summary>Stored alarms of all visible installations, newest first; those of another customer are left out.</summary>
    Task<UnderproductionAlarmListResponse> ListAlarmsAsync(UnderproductionSeverity? severity, bool? ongoing, CancellationToken ct);
}

/// <summary>
/// Connects the API with the ML service (#376, ADR #196). The service has no database, so the API prepares its inputs and
/// stores its outputs. Results are saved only after a successful answer, so an unavailable service leaves the last
/// predictions and alarms in place and nothing else in the API depends on it.
/// </summary>
internal sealed class MlAnalysisService(
    AppDbContext db,
    IMlServiceClient ml,
    IProductionService production,
    ICustomerScope customerScope,
    TimeProvider timeProvider,
    IOptions<MlOptions> options) : IMlAnalysisService
{
    private const int MaxListedAlarms = 100;
    private const string ConcurrentRunMessage = "Another analysis of this installation is running. Try again later.";

    public async Task<MlAnalysisResponse> RunAsync(Guid installationId, CancellationToken ct)
    {
        var plant = await LoadPlantAsync(installationId, ct);
        var now = timeProvider.GetUtcNow();

        var prediction = await PredictAsync(installationId, plant, now, ct);
        var alarms = await DetectAsync(installationId, plant, now, ct);

        return new MlAnalysisResponse(
            installationId,
            prediction?.ModelVersion,
            prediction?.Predictions.Count ?? 0,
            alarms.Count,
            alarms.Count(a => a.Ongoing),
            now);
    }

    public async Task<UnderproductionAlarmListResponse> ListAlarmsAsync(Guid installationId, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visible = db.Set<Installation>().AsNoTracking().Where(i => i.Id == installationId);
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            visible = visible.Where(i => i.CustomerId == own);
        }

        if (!await visible.AnyAsync(ct))
        {
            throw new NotFoundException("Installation", installationId);
        }

        return await ListAsync(db.Set<UnderproductionAlarm>().Where(a => a.InstallationId == installationId), visible, ct);
    }

    public async Task<UnderproductionAlarmListResponse> ListAlarmsAsync(UnderproductionSeverity? severity, bool? ongoing, CancellationToken ct)
    {
        var scope = await customerScope.GetAsync(ct);
        var visible = db.Set<Installation>().AsNoTracking();
        if (scope.IsRestricted)
        {
            var own = scope.OwnCustomerId;
            visible = visible.Where(i => i.CustomerId == own);
        }

        var alarms = db.Set<UnderproductionAlarm>().AsQueryable();
        if (severity is { } wanted)
        {
            alarms = alarms.Where(a => a.Severity == wanted);
        }

        if (ongoing is { } state)
        {
            alarms = alarms.Where(a => a.Ongoing == state);
        }

        return await ListAsync(alarms, visible, ct);
    }

    private static async Task<UnderproductionAlarmListResponse> ListAsync(
        IQueryable<UnderproductionAlarm> alarms, IQueryable<Installation> installations, CancellationToken ct)
    {
        var rows = await alarms
            .AsNoTracking()
            .Join(installations, a => a.InstallationId, i => i.Id, (a, i) => new { Alarm = a, i.Name })
            .OrderByDescending(x => x.Alarm.Start)
            .Take(MaxListedAlarms)
            .ToListAsync(ct);

        return new UnderproductionAlarmListResponse(rows.Select(x => UnderproductionAlarmResponse.From(x.Alarm, x.Name)).ToList());
    }

    private async Task<PlantParameters> LoadPlantAsync(Guid installationId, CancellationToken ct)
    {
        var installation = await db.Set<Installation>()
            .AsNoTracking()
            .Where(i => i.Id == installationId)
            .Select(i => new { i.SupplyPointId, i.PlannedPowerKwp })
            .FirstOrDefaultAsync(ct)
            ?? throw new NotFoundException("Installation", installationId);

        // The same design as the weather refresh: the newest one with coordinates at the supply point.
        var design = await db.Set<FveDesign>()
            .AsNoTracking()
            .Where(d => d.SupplyPointId == installation.SupplyPointId && d.Latitude != null && d.Longitude != null)
            .OrderByDescending(d => d.CreatedAt)
            .FirstOrDefaultAsync(ct)
            ?? throw new DomainException("The installation has no FVE design with coordinates, so it cannot be analysed.");

        var plane = MlInputBuilder.LargestPlane(design.RoofPlanes)
            ?? throw new DomainException("The FVE design of the installation has no roof plane, so it cannot be analysed.");

        // The approved configuration, otherwise the latest one; a design without a saved configuration falls back to the planned power.
        var version = await db.Set<FveDesignVersion>()
            .AsNoTracking()
            .Where(v => v.DesignId == design.Id && (design.ApprovedVersionNumber == null || v.VersionNumber == design.ApprovedVersionNumber))
            .OrderByDescending(v => v.VersionNumber)
            .FirstOrDefaultAsync(ct);

        var dcKwp = version?.DcPowerKwp ?? (double?)installation.PlannedPowerKwp;
        if (dcKwp is not > 0)
        {
            throw new DomainException("The power of the installation is not known, so it cannot be analysed.");
        }

        return new PlantParameters(
            new MlPlant(design.Latitude!.Value, design.Longitude!.Value, dcKwp.Value, plane.TiltDeg, plane.AzimuthDeg, version?.DcAcRatio),
            dcKwp.Value * 1000,
            version is null ? null : version.AcPowerKw * 1000);
    }

    /// <summary>Null when there is no usable forecast yet; the detection still runs over the predictions stored earlier.</summary>
    private async Task<MlPredictionResponse?> PredictAsync(Guid installationId, PlantParameters plant, DateTimeOffset now, CancellationToken ct)
    {
        var forecast = await db.Set<WeatherForecast>()
            .AsNoTracking()
            .Where(f => f.InstallationId == installationId)
            .Select(f => new ForecastHour(
                f.ForecastTime,
                f.ShortwaveRadiationWm2,
                f.DirectNormalIrradianceWm2,
                f.DiffuseRadiationWm2,
                f.CloudCoverPercent,
                f.TemperatureC,
                f.WindSpeedMs))
            .ToListAsync(ct);

        var weather = MlInputBuilder.WeatherHours(forecast);
        if (weather.Count == 0)
        {
            return null;
        }

        var response = await ml.PredictAsync(new MlPredictionRequest(installationId, plant.Plant, weather), ct);
        await ReplacePredictionsAsync(installationId, response, weather, now, ct);
        return response;
    }

    private async Task ReplacePredictionsAsync(
        Guid installationId,
        MlPredictionResponse response,
        IReadOnlyList<MlWeatherHour> weather,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var weatherByHour = weather.ToDictionary(w => w.Timestamp.ToUniversalTime());
        var rows = response.Predictions
            .Select(p =>
            {
                var hour = p.Timestamp.ToUniversalTime();
                weatherByHour.TryGetValue(hour, out var source);
                return StoredPrediction.Create(
                    installationId, hour, p.ExpectedPowerW, p.ExpectedEnergyWh, source?.GhiWm2, source?.CloudCoverPct, response.ModelVersion, now);
            })
            .ToList();
        var hours = rows.Select(r => r.HourStart).ToList();

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            await db.Set<StoredPrediction>()
                .Where(p => p.InstallationId == installationId && hours.Contains(p.HourStart))
                .ExecuteDeleteAsync(ct);
            db.AddRange(rows);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ConcurrentRunMessage);
        }
    }

    private async Task<IReadOnlyList<MlAlarm>> DetectAsync(Guid installationId, PlantParameters plant, DateTimeOffset now, CancellationToken ct)
    {
        // Only finished hours: a running hour has too few samples yet.
        var to = new DateTimeOffset(now.UtcDateTime.Date.AddHours(now.UtcDateTime.Hour), TimeSpan.Zero);
        var alarms = db.Set<UnderproductionAlarm>().Where(a => a.InstallationId == installationId);
        var earliestOngoing = await alarms.Where(a => a.Ongoing).MinAsync(a => (DateTimeOffset?)a.Start, ct);
        var from = MlInputBuilder.DetectionWindowStart(to, options.Value.DetectionLookbackHours, earliestOngoing);

        var expectations = await db.Set<StoredPrediction>()
            .AsNoTracking()
            .Where(p => p.InstallationId == installationId && p.HourStart >= from && p.HourStart < to)
            .Select(p => new HourlyExpectation(p.HourStart, p.ExpectedPowerW, p.GhiWm2, p.CloudCoverPct))
            .ToListAsync(ct);
        if (expectations.Count == 0)
        {
            return [];
        }

        var actual = await production.GetAsync(
            installationId,
            new ProductionPeriod(from, to, ProductionGranularity.Hour, ProductionQuery.DefaultTimeZone),
            ct);
        var response = await ml.DetectAsync(
            new MlDetectionRequest(
                installationId,
                plant.RatedPowerW,
                plant.AcLimitW,
                MlInputBuilder.DetectionPoints(expectations, actual.Points)),
            ct);

        var ids = response.Alarms.Select(a => a.AlarmId).ToList();
        var existing = await alarms.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);
        foreach (var alarm in response.Alarms)
        {
            if (existing.TryGetValue(alarm.AlarmId, out var stored))
            {
                stored.Apply(alarm, now);
            }
            else
            {
                db.Add(UnderproductionAlarm.Create(installationId, alarm, now));
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ConcurrentRunMessage);
        }

        return response.Alarms;
    }

    private sealed record PlantParameters(MlPlant Plant, double RatedPowerW, double? AcLimitW);
}
