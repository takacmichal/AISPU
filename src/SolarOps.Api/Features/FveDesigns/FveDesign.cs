using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.FveDesigns;

/// <summary>
/// Workflow state of a technical PV design (BPMN #62, spike #207).
/// <c>Requested → SiteSurveyDone → InDesign → Approved</c>; <c>SiteSurveyDone → Infeasible</c>; <c>InDesign → Rejected</c>.
/// </summary>
public enum FveDesignStatus
{
    Requested,
    SiteSurveyDone,
    Infeasible,
    InDesign,
    Rejected,
    Approved,
}

/// <summary>Roof covering / construction; determines the mounting system.</summary>
public enum RoofType
{
    PitchedTile,
    PitchedMetal,
    PitchedOther,
    Flat,
    GroundMount,
}

/// <summary>Shape of the customer's electricity consumption during the day (drives the self-consumption estimate).</summary>
public enum ConsumptionProfile
{
    /// <summary>Household, residents away during the day (most consumption in the evening).</summary>
    Household,

    /// <summary>Household with someone at home during the day (home office, retirees, heat pump).</summary>
    HouseholdDaytime,

    /// <summary>Business / office with consumption mostly during working hours.</summary>
    Business,
}

/// <summary>One roof surface usable for panels. Stored as JSON inside the design row.</summary>
public sealed class RoofPlane
{
    public required string Name { get; set; }

    /// <summary>Azimuth in degrees, PVGIS convention: 0 = south, -90 = east, 90 = west, ±180 = north.</summary>
    public double AzimuthDeg { get; set; }

    /// <summary>Tilt from horizontal in degrees (0 = flat, 90 = vertical facade).</summary>
    public double TiltDeg { get; set; }

    /// <summary>Area usable for panels (after setbacks, chimneys, windows) in m².</summary>
    public double UsableAreaM2 { get; set; }

    /// <summary>Annual energy lost to shading in percent (0 = unshaded, 100 = fully shaded).</summary>
    public double ShadingLossPercent { get; set; }

    public RoofType RoofType { get; set; }
}

/// <summary>
/// Technical PV design for a customer's supply point (#76–#78). Customers and supply points live in another
/// module, so they are referenced by id only (no FK / navigation).
/// </summary>
public sealed class FveDesign
{
    private FveDesign()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    /// <summary>Supply point (odberné miesto) the roof and consumption data belong to.</summary>
    public Guid SupplyPointId { get; private set; }

    public FveDesignStatus Status { get; private set; }

    public string? RequestNotes { get; private set; }

    public string? SiteSurveyNotes { get; private set; }

    public Guid? SiteSurveyBy { get; private set; }

    public DateTimeOffset? SiteSurveyAt { get; private set; }

    public string? InfeasibleReason { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public double? AnnualConsumptionKwh { get; private set; }

    public ConsumptionProfile? ConsumptionProfile { get; private set; }

    public List<RoofPlane> RoofPlanes { get; private set; } = [];

    /// <summary>Who approved the design (BPMN gate "Návrh vyhovuje?"); null unless the design is Approved.</summary>
    public Guid? ApprovedBy { get; private set; }

    public DateTimeOffset? ApprovedAt { get; private set; }

    /// <summary>Saved configuration version that was approved; quotes are built from it.</summary>
    public int? ApprovedVersionNumber { get; private set; }

    /// <summary>Reason of the last rework request (gate "Návrh vyhovuje?" = no); kept after the design is approved again.</summary>
    public string? ReworkReason { get; private set; }

    public Guid? ReworkBy { get; private set; }

    public DateTimeOffset? ReworkAt { get; private set; }

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Starts the design process (BPMN #62 "Odovzdať podklady projektantovi").</summary>
    public static FveDesign Request(Guid customerId, Guid supplyPointId, string? notes, Guid userId, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        CustomerId = customerId,
        SupplyPointId = supplyPointId,
        Status = FveDesignStatus.Requested,
        RequestNotes = notes,
        CreatedBy = userId,
        CreatedAt = now,
        UpdatedAt = now,
    };

    /// <summary>Records the result of the on-site survey: <c>Requested → SiteSurveyDone</c>.</summary>
    public void RecordSiteSurvey(string notes, Guid userId, DateTimeOffset now)
    {
        EnsureStatus("record a site survey", FveDesignStatus.Requested);
        SiteSurveyNotes = notes;
        SiteSurveyBy = userId;
        SiteSurveyAt = now;
        Transition(FveDesignStatus.SiteSurveyDone, now);
    }

    /// <summary>Gateway "Realizovateľné?" = no: <c>SiteSurveyDone → Infeasible</c> (terminal).</summary>
    public void MarkInfeasible(string reason, DateTimeOffset now)
    {
        EnsureStatus("mark the design as infeasible", FveDesignStatus.SiteSurveyDone);
        InfeasibleReason = reason;
        Transition(FveDesignStatus.Infeasible, now);
    }

    /// <summary>
    /// Sets roof, consumption and location inputs: <c>SiteSurveyDone → InDesign</c>, or edits them while <c>InDesign</c>.
    /// The roof planes are replaced as a whole.
    /// </summary>
    public void UpdateParameters(
        double latitude,
        double longitude,
        double annualConsumptionKwh,
        ConsumptionProfile profile,
        IReadOnlyCollection<RoofPlane> roofPlanes,
        DateTimeOffset now)
    {
        EnsureStatus("update design parameters", FveDesignStatus.SiteSurveyDone, FveDesignStatus.InDesign);
        if (roofPlanes.Count == 0)
        {
            throw new DomainException("A design needs at least one roof plane.");
        }

        Latitude = latitude;
        Longitude = longitude;
        AnnualConsumptionKwh = annualConsumptionKwh;
        ConsumptionProfile = profile;
        RoofPlanes = [.. roofPlanes];
        Transition(FveDesignStatus.InDesign, now);
    }

    /// <summary>
    /// Gateway "Návrh vyhovuje?" = yes: <c>InDesign → Approved</c>. The newest saved configuration version
    /// (<paramref name="latestVersionNumber"/>, null when none exists) becomes the approved one.
    /// </summary>
    public void Approve(int? latestVersionNumber, Guid userId, DateTimeOffset now)
    {
        EnsureStatus("approve the design", FveDesignStatus.InDesign);
        if (latestVersionNumber is null)
        {
            throw new DomainException("Cannot approve a design without a saved configuration version.");
        }

        ApprovedBy = userId;
        ApprovedAt = now;
        ApprovedVersionNumber = latestVersionNumber;
        Transition(FveDesignStatus.Approved, now);
    }

    /// <summary>
    /// Gateway "Návrh vyhovuje?" = no: the design goes (back) to <c>InDesign</c>. From <c>Approved</c> this
    /// withdraws the approval, so the caller must mark quotes built from the design for recalculation.
    /// </summary>
    public void Rework(string reason, Guid userId, DateTimeOffset now)
    {
        EnsureStatus("send the design to rework", FveDesignStatus.InDesign, FveDesignStatus.Approved);
        ApprovedBy = null;
        ApprovedAt = null;
        ApprovedVersionNumber = null;
        ReworkReason = reason;
        ReworkBy = userId;
        ReworkAt = now;
        Transition(FveDesignStatus.InDesign, now);
    }

    private void EnsureStatus(string action, params FveDesignStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(
                $"Cannot {action} when the design is {Status}; allowed only in: {string.Join(", ", allowed)}.");
        }
    }

    private void Transition(FveDesignStatus to, DateTimeOffset now)
    {
        Status = to;
        UpdatedAt = now;
    }
}
