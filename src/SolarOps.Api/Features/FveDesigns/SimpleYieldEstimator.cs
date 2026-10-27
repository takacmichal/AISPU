namespace SolarOps.Api.Features.FveDesigns;

/// <summary>
/// Deterministic rule-of-thumb estimate without external calls: base specific yield by latitude, scaled per roof
/// plane by tilt, azimuth and shading; DC power is split across planes by usable area; inverter clipping is
/// subtracted. Meant as a placeholder until PVGIS / the prediction service is wired in.
/// </summary>
public sealed class SimpleYieldEstimator : IYieldEstimator
{
    public const string SourceName = "simple-v1";

    private const double BaseYieldAt48Deg = 1000.0;
    private const double YieldPerDegreeLatitude = 12.0;
    private const double OptimalTiltDeg = 35.0;
    private const double ClippingFreeRatio = 1.1;
    private const double ClippingLossPerRatioUnit = 0.1;

    public Task<YieldEstimate> EstimateAsync(YieldEstimateInput input, CancellationToken ct)
    {
        var baseYield = BaseYieldAt48Deg - YieldPerDegreeLatitude * (Math.Clamp(input.Latitude, 35, 60) - 48);

        var totalArea = input.RoofPlanes.Sum(p => p.UsableAreaM2);
        var planeCount = input.RoofPlanes.Count;

        var annualKwh = 0.0;
        foreach (var plane in input.RoofPlanes)
        {
            var share = totalArea > 0 ? plane.UsableAreaM2 / totalArea : 1.0 / planeCount;
            var factor = TiltFactor(plane.TiltDeg) * AzimuthFactor(plane.AzimuthDeg) * (1 - plane.ShadingLossPercent / 100.0);
            annualKwh += input.DcPowerKwp * share * baseYield * factor;
        }

        var ratio = input.AcPowerKw > 0 ? input.DcPowerKwp / input.AcPowerKw : 1.0;
        annualKwh *= 1 - Math.Max(0, ratio - ClippingFreeRatio) * ClippingLossPerRatioUnit;

        var specific = input.DcPowerKwp > 0 ? annualKwh / input.DcPowerKwp : 0;
        return Task.FromResult(new YieldEstimate(annualKwh, specific, SourceName));
    }

    private static double TiltFactor(double tiltDeg) =>
        Math.Max(0.5, 1 - 0.00015 * Math.Pow(tiltDeg - OptimalTiltDeg, 2));

    /// <summary>1.0 facing south, 0.8 east/west, 0.5 north (PVGIS azimuth convention, 0 = south).</summary>
    private static double AzimuthFactor(double azimuthDeg)
    {
        var a = Math.Min(Math.Abs(azimuthDeg), 180);
        return a <= 90
            ? 1 - 0.2 * Math.Pow(a / 90, 2)
            : 0.8 - 0.3 * ((a - 90) / 90);
    }
}
