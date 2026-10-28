using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.FveDesigns;

public sealed class FveDesignTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();

    internal static FveDesign NewDesign() => FveDesign.Request(Guid.CreateVersion7(), Guid.CreateVersion7(), "Rodinný dom", UserId, Now);

    internal static RoofPlane SouthPlane(double area = 30) => new()
    {
        Name = "Juh",
        AzimuthDeg = 0,
        TiltDeg = 35,
        UsableAreaM2 = area,
        ShadingLossPercent = 0,
        RoofType = RoofType.PitchedTile,
    };

    internal static FveDesign DesignInDesign()
    {
        var design = NewDesign();
        design.RecordSiteSurvey("Strecha OK", UserId, Now);
        design.UpdateParameters(48.15, 17.11, 4500, ConsumptionProfile.Household, [SouthPlane()], Now);
        return design;
    }

    [Fact]
    public void Request_StartsInRequested()
    {
        var design = NewDesign();

        Assert.Equal(FveDesignStatus.Requested, design.Status);
        Assert.NotEqual(Guid.Empty, design.Id);
        Assert.Equal(UserId, design.CreatedBy);
        Assert.Empty(design.RoofPlanes);
    }

    [Fact]
    public void RecordSiteSurvey_FromRequested_MovesToSiteSurveyDone()
    {
        var design = NewDesign();

        design.RecordSiteSurvey("Bez tienenia", UserId, Now.AddDays(1));

        Assert.Equal(FveDesignStatus.SiteSurveyDone, design.Status);
        Assert.Equal("Bez tienenia", design.SiteSurveyNotes);
        Assert.Equal(UserId, design.SiteSurveyBy);
        Assert.Equal(Now.AddDays(1), design.SiteSurveyAt);
        Assert.Equal(Now.AddDays(1), design.UpdatedAt);
    }

    [Fact]
    public void RecordSiteSurvey_Twice_Throws()
    {
        var design = NewDesign();
        design.RecordSiteSurvey("ok", UserId, Now);

        Assert.Throws<DomainException>(() => design.RecordSiteSurvey("again", UserId, Now));
    }

    [Fact]
    public void MarkInfeasible_AfterSurvey_MovesToInfeasible()
    {
        var design = NewDesign();
        design.RecordSiteSurvey("Statika nevyhovuje", UserId, Now);

        design.MarkInfeasible("Krov neunesie panely", Now);

        Assert.Equal(FveDesignStatus.Infeasible, design.Status);
        Assert.Equal("Krov neunesie panely", design.InfeasibleReason);
    }

    [Fact]
    public void MarkInfeasible_BeforeSurvey_Throws()
    {
        var design = NewDesign();

        Assert.Throws<DomainException>(() => design.MarkInfeasible("x", Now));
    }

    [Fact]
    public void MarkInfeasible_InDesign_Throws()
    {
        var design = DesignInDesign();

        Assert.Throws<DomainException>(() => design.MarkInfeasible("x", Now));
    }

    [Fact]
    public void UpdateParameters_AfterSurvey_MovesToInDesign()
    {
        var design = NewDesign();
        design.RecordSiteSurvey("ok", UserId, Now);

        design.UpdateParameters(48.7, 21.2, 5200, ConsumptionProfile.HouseholdDaytime, [SouthPlane(40)], Now);

        Assert.Equal(FveDesignStatus.InDesign, design.Status);
        Assert.Equal(48.7, design.Latitude);
        Assert.Equal(21.2, design.Longitude);
        Assert.Equal(5200, design.AnnualConsumptionKwh);
        Assert.Equal(ConsumptionProfile.HouseholdDaytime, design.ConsumptionProfile);
        Assert.Single(design.RoofPlanes);
    }

    [Fact]
    public void UpdateParameters_InDesign_ReplacesRoofPlanes()
    {
        var design = DesignInDesign();
        var east = SouthPlane();
        east.Name = "Východ";
        east.AzimuthDeg = -90;

        design.UpdateParameters(48.15, 17.11, 4500, ConsumptionProfile.Household, [east, SouthPlane()], Now);

        Assert.Equal(FveDesignStatus.InDesign, design.Status);
        Assert.Equal(2, design.RoofPlanes.Count);
        Assert.Equal("Východ", design.RoofPlanes[0].Name);
    }

    [Fact]
    public void UpdateParameters_BeforeSurvey_Throws()
    {
        var design = NewDesign();

        Assert.Throws<DomainException>(() =>
            design.UpdateParameters(48, 17, 4000, ConsumptionProfile.Household, [SouthPlane()], Now));
    }

    [Fact]
    public void UpdateParameters_WhenInfeasible_Throws()
    {
        var design = NewDesign();
        design.RecordSiteSurvey("ok", UserId, Now);
        design.MarkInfeasible("no", Now);

        Assert.Throws<DomainException>(() =>
            design.UpdateParameters(48, 17, 4000, ConsumptionProfile.Household, [SouthPlane()], Now));
    }

    [Fact]
    public void UpdateParameters_WithoutRoofPlanes_Throws()
    {
        var design = NewDesign();
        design.RecordSiteSurvey("ok", UserId, Now);

        Assert.Throws<DomainException>(() =>
            design.UpdateParameters(48, 17, 4000, ConsumptionProfile.Household, [], Now));
    }

    private static readonly Guid ApproverId = Guid.CreateVersion7();

    [Fact]
    public void Approve_InDesignWithVersion_RecordsApprover()
    {
        var design = DesignInDesign();

        design.Approve(2, ApproverId, Now.AddDays(2));

        Assert.Equal(FveDesignStatus.Approved, design.Status);
        Assert.Equal(ApproverId, design.ApprovedBy);
        Assert.Equal(Now.AddDays(2), design.ApprovedAt);
        Assert.Equal(2, design.ApprovedVersionNumber);
        Assert.Equal(Now.AddDays(2), design.UpdatedAt);
    }

    [Fact]
    public void Approve_WithoutSavedVersion_Throws()
    {
        var design = DesignInDesign();

        Assert.Throws<DomainException>(() => design.Approve(null, ApproverId, Now));
        Assert.Equal(FveDesignStatus.InDesign, design.Status);
    }

    [Theory]
    [InlineData(FveDesignStatus.Requested)]
    [InlineData(FveDesignStatus.SiteSurveyDone)]
    [InlineData(FveDesignStatus.Infeasible)]
    [InlineData(FveDesignStatus.Approved)]
    public void Approve_NotInDesign_Throws(FveDesignStatus status)
    {
        var design = status switch
        {
            FveDesignStatus.Requested => NewDesign(),
            FveDesignStatus.SiteSurveyDone => SurveyedDesign(),
            FveDesignStatus.Infeasible => InfeasibleDesign(),
            _ => ApprovedDesign(),
        };

        Assert.Throws<DomainException>(() => design.Approve(1, ApproverId, Now));
    }

    [Fact]
    public void Rework_FromApproved_ReopensDesignAndClearsApproval()
    {
        var design = ApprovedDesign();

        design.Rework("Zákazník chce väčší invertor", ApproverId, Now.AddDays(3));

        Assert.Equal(FveDesignStatus.InDesign, design.Status);
        Assert.Null(design.ApprovedBy);
        Assert.Null(design.ApprovedAt);
        Assert.Null(design.ApprovedVersionNumber);
        Assert.Equal("Zákazník chce väčší invertor", design.ReworkReason);
        Assert.Equal(ApproverId, design.ReworkBy);
        Assert.Equal(Now.AddDays(3), design.ReworkAt);
    }

    [Fact]
    public void Rework_FromInDesign_StaysInDesignAndRecordsReason()
    {
        var design = DesignInDesign();

        design.Rework("Chýba tienenie komína", ApproverId, Now);

        Assert.Equal(FveDesignStatus.InDesign, design.Status);
        Assert.Equal("Chýba tienenie komína", design.ReworkReason);
    }

    [Fact]
    public void Rework_BeforeDesignPhase_Throws()
    {
        Assert.Throws<DomainException>(() => SurveyedDesign().Rework("x", ApproverId, Now));
    }

    [Fact]
    public void UpdateParameters_AfterApproval_Throws()
    {
        var design = ApprovedDesign();

        Assert.Throws<DomainException>(() =>
            design.UpdateParameters(48, 17, 4000, ConsumptionProfile.Household, [SouthPlane()], Now));
    }

    private static FveDesign SurveyedDesign()
    {
        var design = NewDesign();
        design.RecordSiteSurvey("ok", UserId, Now);
        return design;
    }

    private static FveDesign InfeasibleDesign()
    {
        var design = SurveyedDesign();
        design.MarkInfeasible("Tienenie", Now);
        return design;
    }

    private static FveDesign ApprovedDesign()
    {
        var design = DesignInDesign();
        design.Approve(1, ApproverId, Now);
        return design;
    }
}
