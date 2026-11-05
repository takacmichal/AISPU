namespace SolarOps.Api.Features.Production;

/// <summary>Predicted energy of one UTC hour (ADR #196 predicts hourly).</summary>
public sealed record PredictedHour(DateTimeOffset HourStart, double ExpectedEnergyWh);

/// <summary>Prediction of an installation for a period; <see cref="None"/> when there is none.</summary>
public sealed record ProductionPrediction(string? ModelVersion, IReadOnlyList<PredictedHour> Hours)
{
    public static readonly ProductionPrediction None = new(null, []);

    public bool IsAvailable => Hours.Count > 0;
}

/// <summary>Where the comparison (#101) takes predictions from: the hours stored by the analysis (#376).</summary>
public interface IProductionPredictionSource
{
    Task<ProductionPrediction> GetAsync(Guid installationId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct);
}

/// <summary>Pairs actual buckets with predicted hours aggregated to the same buckets.</summary>
public static class ProductionComparison
{
    public static IReadOnlyList<ProductionComparisonPointResponse> Pair(
        IReadOnlyList<ProductionBucket> actual, ProductionPrediction prediction, ProductionPeriod period)
    {
        var zone = period.TimeZone;
        var predicted = prediction.Hours
            .Where(h => h.HourStart >= period.From && h.HourStart < period.To)
            .GroupBy(h => BucketStart(h.HourStart, period.Granularity, zone))
            .ToDictionary(g => g.Key, g => g.Sum(h => h.ExpectedEnergyWh));
        var measured = actual.ToDictionary(b => new DateTimeOffset(DateTime.SpecifyKind(b.PeriodStart, DateTimeKind.Utc)), b => b.EnergyWh);

        return measured.Keys.Union(predicted.Keys)
            .Order()
            .Select(start =>
            {
                double? actualKwh = measured.TryGetValue(start, out var a) ? ProductionCsv.Kilo(a) : null;
                double? predictedKwh = predicted.TryGetValue(start, out var p) ? ProductionCsv.Kilo(p) : null;
                double? deviation = actualKwh is not null && predictedKwh is not null
                    ? Math.Round(actualKwh.Value - predictedKwh.Value, 3)
                    : null;
                double? ratio = actualKwh is not null && predictedKwh is > 0
                    ? Math.Round(a / p, 4)
                    : null;
                return new ProductionComparisonPointResponse(start, actualKwh, predictedKwh, deviation, ratio);
            })
            .ToList();
    }

    /// <summary>Same bucketing as the SQL query: hours as they are, days and months by the local calendar.</summary>
    internal static DateTimeOffset BucketStart(DateTimeOffset instant, ProductionGranularity granularity, TimeZoneInfo zone)
    {
        var utc = instant.ToUniversalTime();
        switch (granularity)
        {
            case ProductionGranularity.FiveMinutes:
                return new DateTimeOffset(utc.Ticks - (utc.Ticks % TimeSpan.FromMinutes(5).Ticks), TimeSpan.Zero);
            case ProductionGranularity.Hour:
                return new DateTimeOffset(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, TimeSpan.Zero);
            default:
                var local = TimeZoneInfo.ConvertTime(utc, zone);
                var start = granularity == ProductionGranularity.Day
                    ? new DateTime(local.Year, local.Month, local.Day, 0, 0, 0, DateTimeKind.Unspecified)
                    : new DateTime(local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
                return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(start, zone), TimeSpan.Zero);
        }
    }
}
