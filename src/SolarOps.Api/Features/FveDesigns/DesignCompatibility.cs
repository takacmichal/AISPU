namespace SolarOps.Api.Features.FveDesigns;

/// <summary>Outcome of the panel/inverter compatibility check of a design configuration.</summary>
public sealed record CompatibilityResult(
    bool IsCompatible,
    double DcPowerKwp,
    double AcPowerKw,
    double DcAcRatio,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Compatibility rules between the panel string and the inverter. The catalog only holds rated power
/// (<c>PowerW</c>), so only the DC/AC ratio and the panel count can be checked. MPPT voltage / current limits
/// need extra catalog fields (see the #77 plan, open questions).
/// </summary>
public static class DesignCompatibility
{
    public const int MinPanelCount = 1;
    public const int MaxPanelCount = 1000;

    /// <summary>Below this DC/AC ratio the inverter is oversized and runs inefficiently.</summary>
    public const double MinDcAcRatio = 0.8;

    /// <summary>Above this DC/AC ratio the inverter is undersized (hard limit).</summary>
    public const double MaxDcAcRatio = 1.5;

    /// <summary>Above this DC/AC ratio production is noticeably clipped (warning only).</summary>
    public const double WarnDcAcRatio = 1.3;

    public static CompatibilityResult Check(int panelCount, int panelPowerW, int inverterPowerW)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (panelCount is < MinPanelCount or > MaxPanelCount)
        {
            errors.Add($"Panel count must be between {MinPanelCount} and {MaxPanelCount}.");
        }

        if (panelPowerW <= 0 || inverterPowerW <= 0)
        {
            errors.Add("Panel and inverter must have a rated power greater than 0.");
            return new CompatibilityResult(false, 0, 0, 0, errors, warnings);
        }

        var dcKwp = panelCount * panelPowerW / 1000.0;
        var acKw = inverterPowerW / 1000.0;
        var ratio = dcKwp / acKw;

        if (errors.Count == 0)
        {
            if (ratio < MinDcAcRatio)
            {
                errors.Add($"DC/AC ratio {ratio:0.00} is below {MinDcAcRatio:0.0}: the inverter is oversized for this many panels.");
            }
            else if (ratio > MaxDcAcRatio)
            {
                errors.Add($"DC/AC ratio {ratio:0.00} is above {MaxDcAcRatio:0.0}: the inverter is too small for this many panels.");
            }
            else if (ratio > WarnDcAcRatio)
            {
                warnings.Add($"DC/AC ratio {ratio:0.00} is above {WarnDcAcRatio:0.0}: expect production losses from inverter clipping.");
            }
        }

        return new CompatibilityResult(errors.Count == 0, dcKwp, acKw, ratio, errors, warnings);
    }
}
