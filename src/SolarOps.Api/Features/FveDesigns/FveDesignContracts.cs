using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.FveDesigns;

/// <summary>Starts a design for a customer's supply point.</summary>
public sealed record CreateFveDesignRequest : IValidatableObject
{
    [Required]
    public Guid? CustomerId { get; init; }

    /// <summary>Supply point (odberné miesto) the design is bound to.</summary>
    [Required]
    public Guid? SupplyPointId { get; init; }

    [MaxLength(FveDesignLimits.NotesMaxLength)]
    public string? Notes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (CustomerId == Guid.Empty)
        {
            yield return new ValidationResult("CustomerId must not be empty.", [nameof(CustomerId)]);
        }

        if (SupplyPointId == Guid.Empty)
        {
            yield return new ValidationResult("SupplyPointId must not be empty.", [nameof(SupplyPointId)]);
        }
    }
}

/// <summary>Result of the on-site survey.</summary>
public sealed record SiteSurveyRequest
{
    [Required, MinLength(1), MaxLength(FveDesignLimits.NotesMaxLength)]
    public string? Notes { get; init; }
}

/// <summary>Reason why the PV plant cannot be built on the site.</summary>
public sealed record MarkInfeasibleRequest
{
    [Required, MinLength(1), MaxLength(FveDesignLimits.NotesMaxLength)]
    public string? Reason { get; init; }
}

/// <summary>Roof, consumption and location inputs of the design.</summary>
public sealed record UpdateDesignParametersRequest : IValidatableObject
{
    [Required, Range(-90.0, 90.0)]
    public double? Latitude { get; init; }

    [Required, Range(-180.0, 180.0)]
    public double? Longitude { get; init; }

    /// <summary>Annual electricity consumption of the supply point in kWh.</summary>
    [Required, Range(0.0, 100_000_000.0)]
    public double? AnnualConsumptionKwh { get; init; }

    [Required]
    public ConsumptionProfile? ConsumptionProfile { get; init; }

    [Required, MinLength(1), MaxLength(FveDesignLimits.MaxRoofPlanes)]
    public List<RoofPlaneRequest>? RoofPlanes { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (RoofPlanes is null)
        {
            yield break;
        }

        for (var i = 0; i < RoofPlanes.Count; i++)
        {
            var plane = RoofPlanes[i];
            if (plane is null)
            {
                yield return new ValidationResult("Roof plane must not be null.", [$"{nameof(RoofPlanes)}[{i}]"]);
                continue;
            }

            foreach (var (member, messages) in DataAnnotationsValidator.Validate(plane, validationContext))
            {
                foreach (var message in messages)
                {
                    yield return new ValidationResult(message, [$"{nameof(RoofPlanes)}[{i}].{member}"]);
                }
            }
        }
    }

    internal List<RoofPlane> ToRoofPlanes() => RoofPlanes!
        .Select(p => new RoofPlane
        {
            Name = p.Name!.Trim(),
            AzimuthDeg = p.AzimuthDeg!.Value,
            TiltDeg = p.TiltDeg!.Value,
            UsableAreaM2 = p.UsableAreaM2!.Value,
            ShadingLossPercent = p.ShadingLossPercent ?? 0,
            RoofType = p.RoofType!.Value,
        })
        .ToList();
}

/// <summary>One roof surface. Orientation (azimuth) and tilt are mandatory.</summary>
public sealed record RoofPlaneRequest
{
    [Required, MinLength(1), MaxLength(FveDesignLimits.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>Azimuth in degrees, PVGIS convention: 0 = south, -90 = east, 90 = west, ±180 = north.</summary>
    [Required, Range(-180.0, 180.0)]
    public double? AzimuthDeg { get; init; }

    /// <summary>Tilt from horizontal in degrees, 0 (flat) to 90 (vertical).</summary>
    [Required, Range(0.0, 90.0)]
    public double? TiltDeg { get; init; }

    /// <summary>Area usable for panels in m².</summary>
    [Required, Range(0.1, 100_000.0)]
    public double? UsableAreaM2 { get; init; }

    /// <summary>Annual shading loss in percent (0–100). Defaults to 0.</summary>
    [Range(0.0, 100.0)]
    public double? ShadingLossPercent { get; init; }

    [Required]
    public RoofType? RoofType { get; init; }
}

public sealed record RoofPlaneResponse(
    string Name,
    double AzimuthDeg,
    double TiltDeg,
    double UsableAreaM2,
    double ShadingLossPercent,
    RoofType RoofType);

public sealed record SiteSurveyResponse(string? Notes, Guid? SurveyedBy, DateTimeOffset? SurveyedAt);

public sealed record FveDesignResponse(
    Guid Id,
    Guid CustomerId,
    Guid SupplyPointId,
    FveDesignStatus Status,
    string? RequestNotes,
    SiteSurveyResponse? SiteSurvey,
    string? InfeasibleReason,
    double? Latitude,
    double? Longitude,
    double? AnnualConsumptionKwh,
    ConsumptionProfile? ConsumptionProfile,
    IReadOnlyList<RoofPlaneResponse> RoofPlanes,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static FveDesignResponse From(FveDesign d) => new(
        d.Id,
        d.CustomerId,
        d.SupplyPointId,
        d.Status,
        d.RequestNotes,
        d.SiteSurveyAt is null ? null : new SiteSurveyResponse(d.SiteSurveyNotes, d.SiteSurveyBy, d.SiteSurveyAt),
        d.InfeasibleReason,
        d.Latitude,
        d.Longitude,
        d.AnnualConsumptionKwh,
        d.ConsumptionProfile,
        d.RoofPlanes
            .Select(p => new RoofPlaneResponse(p.Name, p.AzimuthDeg, p.TiltDeg, p.UsableAreaM2, p.ShadingLossPercent, p.RoofType))
            .ToList(),
        d.CreatedBy,
        d.CreatedAt,
        d.UpdatedAt);
}
