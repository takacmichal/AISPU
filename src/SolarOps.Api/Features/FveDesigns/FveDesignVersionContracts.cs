using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.FveDesigns;

/// <summary>Proposed system: a panel type with a count and an inverter, both from the catalog.</summary>
public sealed record ConfigureDesignRequest
{
    [Required]
    public Guid? PanelId { get; init; }

    [Required, Range(DesignCompatibility.MinPanelCount, DesignCompatibility.MaxPanelCount)]
    public int? PanelCount { get; init; }

    [Required]
    public Guid? InverterId { get; init; }
}

public sealed record CompatibilityResponse(
    bool IsCompatible,
    double DcPowerKwp,
    double AcPowerKw,
    double DcAcRatio,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public static CompatibilityResponse From(CompatibilityResult r) =>
        new(r.IsCompatible, r.DcPowerKwp, r.AcPowerKw, r.DcAcRatio, r.Errors, r.Warnings);
}

public sealed record YieldEstimateResponse(double AnnualKwh, double SpecificYieldKwhPerKwp, string Source)
{
    public static YieldEstimateResponse From(YieldEstimate e) => new(e.AnnualKwh, e.SpecificYieldKwhPerKwp, e.Source);
}

/// <summary>Result of checking a configuration without saving it. <c>Estimate</c> is null when incompatible.</summary>
public sealed record DesignConfigurationPreviewResponse(CompatibilityResponse Compatibility, YieldEstimateResponse? Estimate);

public sealed record FveDesignVersionResponse(
    Guid Id,
    Guid DesignId,
    int VersionNumber,
    Guid PanelId,
    string PanelCode,
    string PanelName,
    int PanelPowerW,
    decimal PanelUnitPrice,
    int PanelCount,
    Guid InverterId,
    string InverterCode,
    string InverterName,
    int InverterPowerW,
    decimal InverterUnitPrice,
    double DcPowerKwp,
    double AcPowerKw,
    double DcAcRatio,
    IReadOnlyList<string> Warnings,
    double AnnualYieldKwh,
    double SpecificYieldKwhPerKwp,
    string EstimateSource,
    Guid CreatedBy,
    DateTimeOffset CreatedAt)
{
    public static FveDesignVersionResponse From(FveDesignVersion v) => new(
        v.Id,
        v.DesignId,
        v.VersionNumber,
        v.PanelId,
        v.PanelCode,
        v.PanelName,
        v.PanelPowerW,
        v.PanelUnitPrice,
        v.PanelCount,
        v.InverterId,
        v.InverterCode,
        v.InverterName,
        v.InverterPowerW,
        v.InverterUnitPrice,
        v.DcPowerKwp,
        v.AcPowerKw,
        v.DcAcRatio,
        v.Warnings,
        v.AnnualYieldKwh,
        v.SpecificYieldKwhPerKwp,
        v.EstimateSource,
        v.CreatedBy,
        v.CreatedAt);
}
