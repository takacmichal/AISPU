using SolarOps.Api.Features.Components;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.FveDesigns;

/// <summary>
/// Immutable saved configuration of a design (panels + inverter + yield estimate). Saving never edits an existing
/// row, it appends the next version number. Component code, name, power and price are copied from the catalog,
/// so later catalog changes do not alter saved versions.
/// </summary>
public sealed class FveDesignVersion
{
    private FveDesignVersion()
    {
        PanelCode = PanelName = InverterCode = InverterName = EstimateSource = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid DesignId { get; private set; }

    /// <summary>1-based, unique within the design.</summary>
    public int VersionNumber { get; private set; }

    public Guid PanelId { get; private set; }

    public string PanelCode { get; private set; }

    public string PanelName { get; private set; }

    public int PanelPowerW { get; private set; }

    public decimal PanelUnitPrice { get; private set; }

    public int PanelCount { get; private set; }

    public Guid InverterId { get; private set; }

    public string InverterCode { get; private set; }

    public string InverterName { get; private set; }

    public int InverterPowerW { get; private set; }

    public decimal InverterUnitPrice { get; private set; }

    public double DcPowerKwp { get; private set; }

    public double AcPowerKw { get; private set; }

    public double DcAcRatio { get; private set; }

    public List<string> Warnings { get; private set; } = [];

    public double AnnualYieldKwh { get; private set; }

    public double SpecificYieldKwhPerKwp { get; private set; }

    public string EstimateSource { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Checks everything except compatibility limits that a preview may report instead of failing: design state,
    /// component types and activity. Returns the compatibility result.
    /// </summary>
    public static CompatibilityResult Evaluate(FveDesign design, CatalogComponent panel, int panelCount, CatalogComponent inverter)
    {
        if (design.Status != FveDesignStatus.InDesign)
        {
            throw new DomainException(
                $"Cannot configure a design that is {design.Status}; roof and consumption parameters must be set first (status InDesign).");
        }

        var errors = new Dictionary<string, string[]>();
        if (panel.Type != ComponentType.Panel)
        {
            errors["PanelId"] = ["Component is not a panel."];
        }

        if (inverter.Type != ComponentType.Inverter)
        {
            errors["InverterId"] = ["Component is not an inverter."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        panel.EnsureActive();
        inverter.EnsureActive();

        return DesignCompatibility.Check(panelCount, panel.PowerW ?? 0, inverter.PowerW ?? 0);
    }

    public static FveDesignVersion Create(
        FveDesign design,
        CatalogComponent panel,
        int panelCount,
        CatalogComponent inverter,
        int versionNumber,
        YieldEstimate estimate,
        Guid userId,
        DateTimeOffset now)
    {
        var compatibility = Evaluate(design, panel, panelCount, inverter);
        if (!compatibility.IsCompatible)
        {
            throw new RequestValidationException("PanelCount", string.Join(" ", compatibility.Errors));
        }

        return new FveDesignVersion
        {
            Id = Guid.CreateVersion7(),
            DesignId = design.Id,
            VersionNumber = versionNumber,
            PanelId = panel.Id,
            PanelCode = panel.Code,
            PanelName = panel.Name,
            PanelPowerW = panel.PowerW!.Value,
            PanelUnitPrice = panel.UnitPrice,
            PanelCount = panelCount,
            InverterId = inverter.Id,
            InverterCode = inverter.Code,
            InverterName = inverter.Name,
            InverterPowerW = inverter.PowerW!.Value,
            InverterUnitPrice = inverter.UnitPrice,
            DcPowerKwp = compatibility.DcPowerKwp,
            AcPowerKw = compatibility.AcPowerKw,
            DcAcRatio = compatibility.DcAcRatio,
            Warnings = [.. compatibility.Warnings],
            AnnualYieldKwh = estimate.AnnualKwh,
            SpecificYieldKwhPerKwp = estimate.SpecificYieldKwhPerKwp,
            EstimateSource = estimate.Source,
            CreatedBy = userId,
            CreatedAt = now,
        };
    }
}
