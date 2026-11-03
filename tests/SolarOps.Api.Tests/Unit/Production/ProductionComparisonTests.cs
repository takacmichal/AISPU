using SolarOps.Api.Features.Production;

namespace SolarOps.Api.Tests.Unit.Production;

public sealed class ProductionComparisonTests
{
    private static readonly DateTimeOffset From = new(2026, 6, 20, 22, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = new(2026, 6, 22, 22, 0, 0, TimeSpan.Zero);

    private static ProductionPeriod Period(ProductionGranularity granularity) =>
        new(From, To, granularity, "Europe/Bratislava");

    private static DateTime Utc(int day, int hour) => new(2026, 6, day, hour, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void WithoutPrediction_EveryPointHasNullPrediction()
    {
        var actual = new[] { new ProductionBucket(Utc(21, 8), 2000, 2500, 12) };

        var points = ProductionComparison.Pair(actual, ProductionPrediction.None, Period(ProductionGranularity.Hour));

        var point = Assert.Single(points);
        Assert.Equal(new DateTimeOffset(Utc(21, 8)), point.PeriodStart);
        Assert.Equal(2.0, point.ActualEnergyKwh);
        Assert.Null(point.PredictedEnergyKwh);
        Assert.Null(point.DeviationKwh);
        Assert.Null(point.Ratio);
    }

    [Fact]
    public void HourlyPrediction_IsPairedByHour_AndMissingActualIsNull()
    {
        var actual = new[] { new ProductionBucket(Utc(21, 8), 1700, 2500, 12) };
        var prediction = new ProductionPrediction("lgbm-1", [
            new PredictedHour(new DateTimeOffset(Utc(21, 8)), 2000),
            new PredictedHour(new DateTimeOffset(Utc(21, 9)), 2500),
        ]);

        var points = ProductionComparison.Pair(actual, prediction, Period(ProductionGranularity.Hour));

        Assert.Equal(2, points.Count);
        Assert.Equal(1.7, points[0].ActualEnergyKwh);
        Assert.Equal(2.0, points[0].PredictedEnergyKwh);
        Assert.Equal(-0.3, points[0].DeviationKwh!.Value, 6);
        Assert.Equal(0.85, points[0].Ratio!.Value, 6);
        Assert.Null(points[1].ActualEnergyKwh);
        Assert.Equal(2.5, points[1].PredictedEnergyKwh);
        Assert.Null(points[1].Ratio);
    }

    [Fact]
    public void DailyComparison_BucketsPredictedHoursByLocalDay()
    {
        // Local day 2026-06-21 in Bratislava starts at 2026-06-20T22:00Z.
        var dayStart = new DateTime(2026, 6, 20, 22, 0, 0, DateTimeKind.Utc);
        var actual = new[] { new ProductionBucket(dayStart, 30_000, 7000, 288) };
        var prediction = new ProductionPrediction("lgbm-1", [
            new PredictedHour(new DateTimeOffset(Utc(20, 22)), 0),      // 00:00 local, same day
            new PredictedHour(new DateTimeOffset(Utc(21, 10)), 20_000),
            new PredictedHour(new DateTimeOffset(Utc(21, 21)), 5_000),  // 23:00 local, same day
            new PredictedHour(new DateTimeOffset(Utc(21, 22)), 1_000),  // 00:00 local, next day
            new PredictedHour(new DateTimeOffset(Utc(19, 10)), 9_999),  // outside the period
        ]);

        var points = ProductionComparison.Pair(actual, prediction, Period(ProductionGranularity.Day));

        Assert.Equal(2, points.Count);
        Assert.Equal(new DateTimeOffset(dayStart), points[0].PeriodStart);
        Assert.Equal(30.0, points[0].ActualEnergyKwh);
        Assert.Equal(25.0, points[0].PredictedEnergyKwh);
        Assert.Equal(1.2, points[0].Ratio!.Value, 6);
        Assert.Equal(new DateTimeOffset(Utc(21, 22)), points[1].PeriodStart);
        Assert.Equal(1.0, points[1].PredictedEnergyKwh);
    }
}
