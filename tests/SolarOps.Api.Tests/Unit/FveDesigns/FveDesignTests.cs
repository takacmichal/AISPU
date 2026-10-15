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
}
