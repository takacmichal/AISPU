using SolarOps.Api.Features.Components;
using SolarOps.Api.Features.FveDesigns;
using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Tests.Unit.FveDesigns;

public sealed class FveDesignVersionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.CreateVersion7();
    private static readonly YieldEstimate Estimate = new(5400, 1000, "test");

    private static CatalogComponent Component(ComponentType type, string code, int power, decimal price = 100m) =>
        CatalogComponent.Create(new ComponentDetails(type, code, $"Name {code}", "Acme", power, price), Now);

    private static FveDesignVersion Create(
        FveDesign? design = null, CatalogComponent? panel = null, CatalogComponent? inverter = null, int count = 12, int number = 1) =>
        FveDesignVersion.Create(
            design ?? FveDesignTests.DesignInDesign(),
            panel ?? Component(ComponentType.Panel, "PAN-450", 450, 120.5m),
            count,
            inverter ?? Component(ComponentType.Inverter, "INV-5K", 5000, 950m),
            number,
            Estimate,
            UserId,
            Now);

    [Fact]
    public void Create_SnapshotsComponentsAndComputedValues()
    {
        var version = Create();

        Assert.Equal(1, version.VersionNumber);
        Assert.Equal("PAN-450", version.PanelCode);
        Assert.Equal(450, version.PanelPowerW);
        Assert.Equal(120.5m, version.PanelUnitPrice);
        Assert.Equal(12, version.PanelCount);
        Assert.Equal("INV-5K", version.InverterCode);
        Assert.Equal(5000, version.InverterPowerW);
        Assert.Equal(5.4, version.DcPowerKwp, 3);
        Assert.Equal(1.08, version.DcAcRatio, 3);
        Assert.Equal(5400, version.AnnualYieldKwh);
        Assert.Equal(UserId, version.CreatedBy);
        Assert.Equal(Now, version.CreatedAt);
    }

    [Theory]
    [InlineData(FveDesignStatus.Requested)]
    [InlineData(FveDesignStatus.SiteSurveyDone)]
    public void Create_DesignNotInDesign_Throws(FveDesignStatus status)
    {
        var design = FveDesignTests.NewDesign();
        if (status == FveDesignStatus.SiteSurveyDone)
        {
            design.RecordSiteSurvey("ok", UserId, Now);
        }

        Assert.Throws<DomainException>(() => Create(design));
    }

    [Fact]
    public void Create_IncompatibleConfiguration_ThrowsValidation()
    {
        var ex = Assert.Throws<RequestValidationException>(() => Create(count: 40));

        Assert.Contains("PanelCount", ex.Errors.Keys);
    }

    [Fact]
    public void Create_WrongComponentType_ThrowsValidation()
    {
        var labor = CatalogComponent.Create(new ComponentDetails(ComponentType.Labor, "LAB", "Labor", null, null, 30m), Now);

        var ex = Assert.Throws<RequestValidationException>(() => Create(panel: labor));

        Assert.Contains("PanelId", ex.Errors.Keys);
    }

    [Fact]
    public void Create_InactiveComponent_ThrowsDomainException()
    {
        var inverter = Component(ComponentType.Inverter, "INV-OLD", 5000);
        inverter.Deactivate(Now);

        Assert.Throws<DomainException>(() => Create(inverter: inverter));
    }
}
