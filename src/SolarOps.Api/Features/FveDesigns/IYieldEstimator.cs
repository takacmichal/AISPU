namespace SolarOps.Api.Features.FveDesigns;

/// <summary>Inputs of an annual yield estimate. Roof planes carry orientation, tilt and shading.</summary>
public sealed record YieldEstimateInput(
    double Latitude,
    double Longitude,
    double DcPowerKwp,
    double AcPowerKw,
    IReadOnlyList<RoofPlane> RoofPlanes);

/// <summary>Estimated annual production of the whole system.</summary>
/// <param name="AnnualKwh">Expected energy per year in kWh.</param>
/// <param name="SpecificYieldKwhPerKwp">Annual energy per installed kWp.</param>
/// <param name="Source">Name of the estimator that produced the value (stored with the version).</param>
public sealed record YieldEstimate(double AnnualKwh, double SpecificYieldKwhPerKwp, string Source);

/// <summary>
/// Seam for the annual production estimate. <see cref="SimpleYieldEstimator"/> is the deterministic default;
/// a PVGIS or ML prediction service client can replace it by registering another implementation.
/// </summary>
public interface IYieldEstimator
{
    Task<YieldEstimate> EstimateAsync(YieldEstimateInput input, CancellationToken ct);
}
