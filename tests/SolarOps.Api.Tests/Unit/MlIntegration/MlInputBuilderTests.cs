using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Features.MlIntegration;
using SolarOps.Api.Features.Production;

namespace SolarOps.Api.Tests.Unit.MlIntegration;

public sealed class MlInputBuilderTests
{
    private static readonly DateTimeOffset Noon = new(2026, 6, 21, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WeatherHours_ShiftsEndLabelledForecastToHourStart()
    {
        var hours = MlInputBuilder.WeatherHours([Forecast(Noon.UtcDateTime)]);

        Assert.Equal(Noon.AddHours(-1), Assert.Single(hours).Timestamp);
    }

    [Fact]
    public void WeatherHours_SkipsHoursWithoutTemperatureOrIrradiance()
    {
        var hours = MlInputBuilder.WeatherHours([
            Forecast(Noon.UtcDateTime, temperature: null),
            Forecast(Noon.UtcDateTime.AddHours(1), ghi: null),
            Forecast(Noon.UtcDateTime.AddHours(2)),
        ]);

        Assert.Equal(Noon.AddHours(1), Assert.Single(hours).Timestamp);
    }

    [Fact]
    public void WeatherHours_OrdersAndKeepsAtMostTheLimitOfTheService()
    {
        var rows = Enumerable.Range(0, MlInputBuilder.MaxWeatherHours + 10)
            .Reverse()
            .Select(i => Forecast(Noon.UtcDateTime.AddHours(i)))
            .ToList();

        var hours = MlInputBuilder.WeatherHours(rows);

        Assert.Equal(MlInputBuilder.MaxWeatherHours, hours.Count);
        Assert.Equal(Noon.AddHours(-1), hours[0].Timestamp);
        Assert.True(hours.Zip(hours.Skip(1)).All(pair => pair.First.Timestamp < pair.Second.Timestamp));
    }

    [Fact]
    public void LargestPlane_PicksTheBiggestRoofSurface()
    {
        var plane = MlInputBuilder.LargestPlane([Plane("Východ", -90, 10), Plane("Juh", 0, 30), Plane("Západ", 90, 20)]);

        Assert.Equal("Juh", plane!.Name);
        Assert.Null(MlInputBuilder.LargestPlane([]));
    }

    [Fact]
    public void DetectionPoints_LabelsHourByItsEndAndComputesMeanPowerAndCoverage()
    {
        var predictions = new[] { new HourlyExpectation(Noon, 2000, 500, 20) };
        var actual = new[] { new ProductionPointResponse(Noon, 1.5, 2.5, 9) };

        var point = Assert.Single(MlInputBuilder.DetectionPoints(predictions, actual));

        Assert.Equal(Noon.AddHours(1), point.Timestamp);
        Assert.Equal(1500, point.ActualPowerW);
        Assert.Equal(2000, point.ExpectedPowerW);
        Assert.Equal(500, point.GhiWm2);
        Assert.Equal(20, point.CloudCoverPct);
        Assert.Equal(0.75, point.SampleCoverage);
    }

    [Fact]
    public void DetectionPoints_HourWithoutTelemetryHasNoActualPowerAndNoCoverage()
    {
        var point = Assert.Single(MlInputBuilder.DetectionPoints([new HourlyExpectation(Noon, 2000, 500, 20)], []));

        Assert.Null(point.ActualPowerW);
        Assert.Equal(0, point.SampleCoverage);
    }

    [Fact]
    public void DetectionPoints_CoverageIsCappedForDenserTelemetry()
    {
        var point = Assert.Single(MlInputBuilder.DetectionPoints(
            [new HourlyExpectation(Noon, 2000, 500, 20)],
            [new ProductionPointResponse(Noon, 1.5, 2.5, 60)]));

        Assert.Equal(1, point.SampleCoverage);
    }

    [Fact]
    public void DetectionWindowStart_DefaultsToTheLookback()
    {
        Assert.Equal(Noon.AddHours(-72), MlInputBuilder.DetectionWindowStart(Noon, 72, earliestOngoingStart: null));
    }

    [Fact]
    public void DetectionWindowStart_ReachesBackToTheFirstHourOfAnOngoingAlarm()
    {
        // The alarm start is labelled by the end of its first hour and its id depends on the start, so that hour must stay in the window.
        var start = MlInputBuilder.DetectionWindowStart(Noon, 72, Noon.AddHours(-100));

        Assert.Equal(Noon.AddHours(-101), start);
    }

    [Fact]
    public void DetectionWindowStart_IsCappedByTheLimitOfTheService()
    {
        var start = MlInputBuilder.DetectionWindowStart(Noon, 72, Noon.AddHours(-(MlInputBuilder.MaxDetectionHours + 500)));

        Assert.Equal(Noon.AddHours(-MlInputBuilder.MaxDetectionHours), start);
    }

    private static RoofPlane Plane(string name, double azimuth, double area) => new()
    {
        Name = name,
        AzimuthDeg = azimuth,
        TiltDeg = 35,
        UsableAreaM2 = area,
        ShadingLossPercent = 0,
        RoofType = RoofType.PitchedTile,
    };

    private static ForecastHour Forecast(DateTime time, double? ghi = 500, double? temperature = 20) =>
        new(time, ghi, 400, 100, 20, temperature, 2);
}
