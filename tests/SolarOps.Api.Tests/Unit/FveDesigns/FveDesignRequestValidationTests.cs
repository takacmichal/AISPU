using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.FveDesigns;

public sealed class FveDesignRequestValidationTests
{
    private static RoofPlaneRequest ValidPlane() => new()
    {
        Name = "Juh",
        AzimuthDeg = 0,
        TiltDeg = 35,
        UsableAreaM2 = 30,
        ShadingLossPercent = 5,
        RoofType = RoofType.PitchedTile,
    };

    private static UpdateDesignParametersRequest ValidParameters(params RoofPlaneRequest[] planes) => new()
    {
        Latitude = 48.15,
        Longitude = 17.11,
        AnnualConsumptionKwh = 4500,
        ConsumptionProfile = ConsumptionProfile.Household,
        RoofPlanes = planes.Length == 0 ? [ValidPlane()] : [.. planes],
    };

    [Fact]
    public void Parameters_Valid_HasNoErrors()
    {
        Assert.Empty(DataAnnotationsValidator.Validate(ValidParameters()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void Parameters_TiltOnBoundary_IsValid(double tilt)
    {
        var errors = DataAnnotationsValidator.Validate(ValidParameters(ValidPlane() with { TiltDeg = tilt }));

        Assert.Empty(errors);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(90.1)]
    [InlineData(120)]
    public void Parameters_TiltOutOfRange_IsInvalid(double tilt)
    {
        var errors = DataAnnotationsValidator.Validate(ValidParameters(ValidPlane() with { TiltDeg = tilt }));

        Assert.Contains("RoofPlanes[0].TiltDeg", errors.Keys);
    }

    [Fact]
    public void Parameters_MissingOrientationAndTilt_AreRequired()
    {
        var errors = DataAnnotationsValidator.Validate(
            ValidParameters(ValidPlane(), ValidPlane() with { AzimuthDeg = null, TiltDeg = null }));

        Assert.Contains("RoofPlanes[1].AzimuthDeg", errors.Keys);
        Assert.Contains("RoofPlanes[1].TiltDeg", errors.Keys);
        Assert.DoesNotContain("RoofPlanes[0].AzimuthDeg", errors.Keys);
    }

    [Theory]
    [InlineData(-180.5)]
    [InlineData(181)]
    public void Parameters_AzimuthOutOfRange_IsInvalid(double azimuth)
    {
        var errors = DataAnnotationsValidator.Validate(ValidParameters(ValidPlane() with { AzimuthDeg = azimuth }));

        Assert.Contains("RoofPlanes[0].AzimuthDeg", errors.Keys);
    }

    [Fact]
    public void Parameters_InvalidAreaAndShading_AreReported()
    {
        var errors = DataAnnotationsValidator.Validate(
            ValidParameters(ValidPlane() with { UsableAreaM2 = 0, ShadingLossPercent = 101 }));

        Assert.Contains("RoofPlanes[0].UsableAreaM2", errors.Keys);
        Assert.Contains("RoofPlanes[0].ShadingLossPercent", errors.Keys);
    }

    [Fact]
    public void Parameters_MissingTopLevelFields_AreRequired()
    {
        var errors = DataAnnotationsValidator.Validate(new UpdateDesignParametersRequest());

        Assert.Contains("Latitude", errors.Keys);
        Assert.Contains("Longitude", errors.Keys);
        Assert.Contains("AnnualConsumptionKwh", errors.Keys);
        Assert.Contains("ConsumptionProfile", errors.Keys);
        Assert.Contains("RoofPlanes", errors.Keys);
    }

    [Fact]
    public void Parameters_EmptyRoofPlanes_IsInvalid()
    {
        var errors = DataAnnotationsValidator.Validate(ValidParameters() with { RoofPlanes = [] });

        Assert.Contains("RoofPlanes", errors.Keys);
    }

    [Fact]
    public void Create_MissingSupplyPoint_IsRequired()
    {
        var errors = DataAnnotationsValidator.Validate(new CreateFveDesignRequest { CustomerId = Guid.CreateVersion7() });

        Assert.Contains("SupplyPointId", errors.Keys);
    }

    [Fact]
    public void Create_EmptyGuids_AreInvalid()
    {
        var errors = DataAnnotationsValidator.Validate(new CreateFveDesignRequest { CustomerId = Guid.Empty, SupplyPointId = Guid.Empty });

        Assert.Contains("CustomerId", errors.Keys);
        Assert.Contains("SupplyPointId", errors.Keys);
    }
}
