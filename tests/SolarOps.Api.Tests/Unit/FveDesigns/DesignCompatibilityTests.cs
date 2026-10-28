using SolarOps.Api.Features.FveDesigns;

namespace SolarOps.Api.Tests.Unit.FveDesigns;

public sealed class DesignCompatibilityTests
{
    [Fact]
    public void Check_BalancedSystem_IsCompatibleWithoutWarnings()
    {
        // 12 x 450 W = 5.4 kWp on a 5 kW inverter -> 1.08
        var result = DesignCompatibility.Check(12, 450, 5000);

        Assert.True(result.IsCompatible);
        Assert.Equal(5.4, result.DcPowerKwp, 3);
        Assert.Equal(5.0, result.AcPowerKw, 3);
        Assert.Equal(1.08, result.DcAcRatio, 3);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Check_HighRatioWithinLimit_WarnsAboutClipping()
    {
        // 15 x 450 = 6.75 kWp / 5 kW = 1.35 -> above the warning limit, below the maximum
        var result = DesignCompatibility.Check(15, 450, 5000);

        Assert.True(result.IsCompatible);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Check_RatioAboveMax_IsIncompatible()
    {
        // 20 x 450 = 9 kWp / 5 kW = 1.8
        var result = DesignCompatibility.Check(20, 450, 5000);

        Assert.False(result.IsCompatible);
        Assert.Single(result.Errors);
    }

    [Fact]
    public void Check_RatioBelowMin_IsIncompatible()
    {
        // 4 x 450 = 1.8 kWp / 5 kW = 0.36
        var result = DesignCompatibility.Check(4, 450, 5000);

        Assert.False(result.IsCompatible);
        Assert.Single(result.Errors);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1001)]
    public void Check_PanelCountOutOfRange_IsIncompatible(int count)
    {
        var result = DesignCompatibility.Check(count, 450, 5000);

        Assert.False(result.IsCompatible);
        Assert.NotEmpty(result.Errors);
    }

    [Theory]
    [InlineData(10, 0, 5000)]
    [InlineData(10, 450, 0)]
    [InlineData(10, -450, 5000)]
    public void Check_NonPositivePower_IsIncompatibleWithZeroedResult(int count, int panelW, int inverterW)
    {
        var result = DesignCompatibility.Check(count, panelW, inverterW);

        Assert.False(result.IsCompatible);
        Assert.Equal(0, result.DcPowerKwp);
        Assert.Equal(0, result.AcPowerKw);
        Assert.Equal(0, result.DcAcRatio);
        Assert.NotEmpty(result.Errors);
    }
}
