using SolarOps.Api.Features.FveDesigns;

namespace SolarOps.Api.Tests.Unit.FveDesigns;

public sealed class SimpleYieldEstimatorTests
{
    private readonly SimpleYieldEstimator _estimator = new();

    private static RoofPlane Plane(double azimuth = 0, double tilt = 35, double area = 30, double shading = 0) => new()
    {
        Name = "P",
        AzimuthDeg = azimuth,
        TiltDeg = tilt,
        UsableAreaM2 = area,
        ShadingLossPercent = shading,
        RoofType = RoofType.PitchedTile,
    };

    private Task<YieldEstimate> EstimateAsync(double dcKwp, params RoofPlane[] planes) =>
        _estimator.EstimateAsync(new YieldEstimateInput(48.15, 17.11, dcKwp, dcKwp, planes), CancellationToken.None);

    [Fact]
    public async Task Estimate_SouthOptimalTilt_GivesPlausibleSlovakYield()
    {
        var estimate = await EstimateAsync(5, Plane());

        Assert.InRange(estimate.SpecificYieldKwhPerKwp, 900, 1150);
        Assert.Equal(estimate.SpecificYieldKwhPerKwp * 5, estimate.AnnualKwh, 3);
        Assert.False(string.IsNullOrWhiteSpace(estimate.Source));
    }

    [Fact]
    public async Task Estimate_IsDeterministic()
    {
        var a = await EstimateAsync(5, Plane(), Plane(-90, 30, 20));
        var b = await EstimateAsync(5, Plane(), Plane(-90, 30, 20));

        Assert.Equal(a, b);
    }

    [Fact]
    public async Task Estimate_EastIsLowerThanSouth_NorthIsLowest()
    {
        var south = await EstimateAsync(5, Plane(0));
        var east = await EstimateAsync(5, Plane(-90));
        var north = await EstimateAsync(5, Plane(180));

        Assert.True(south.AnnualKwh > east.AnnualKwh);
        Assert.True(east.AnnualKwh > north.AnnualKwh);
    }

    [Fact]
    public async Task Estimate_ShadingReducesYieldProportionally()
    {
        var clear = await EstimateAsync(5, Plane());
        var shaded = await EstimateAsync(5, Plane(shading: 20));

        Assert.Equal(clear.AnnualKwh * 0.8, shaded.AnnualKwh, 3);
    }

    [Fact]
    public async Task Estimate_SplitsPowerAcrossPlanesByArea()
    {
        var onlySouth = await EstimateAsync(6, Plane(0, area: 30));
        var onlyEast = await EstimateAsync(6, Plane(-90, area: 30));
        var mixed = await EstimateAsync(6, Plane(0, area: 30), Plane(-90, area: 30));

        Assert.Equal((onlySouth.AnnualKwh + onlyEast.AnnualKwh) / 2, mixed.AnnualKwh, 3);
    }

    [Fact]
    public async Task Estimate_ClippingReducesYieldWhenInverterIsSmall()
    {
        var input = new YieldEstimateInput(48.15, 17.11, 7.5, 5, [Plane()]);
        var clipped = await _estimator.EstimateAsync(input, CancellationToken.None);
        var unclipped = await _estimator.EstimateAsync(input with { AcPowerKw = 7.5 }, CancellationToken.None);

        Assert.True(clipped.AnnualKwh < unclipped.AnnualKwh);
    }
}
