using SolarOps.Api.Features.Customers;

namespace SolarOps.Api.Tests.Unit.Customers;

public sealed class SlovakIdentifiersTests
{
    [Theory]
    [InlineData("35763469")] // Slovak Telekom, a.s.
    [InlineData("31333532")] // ESET, spol. s r.o.
    public void IsValidIco_AcceptsRealIcos(string ico) => Assert.True(SlovakIdentifiers.IsValidIco(ico));

    [Theory]
    [InlineData("35763468")] // wrong check digit
    [InlineData("3576346")] // too short
    [InlineData("357634690")] // too long
    [InlineData("3576346A")]
    [InlineData("")]
    public void IsValidIco_RejectsInvalidValues(string ico) => Assert.False(SlovakIdentifiers.IsValidIco(ico));

    [Theory]
    [InlineData("2020273893", true)]
    [InlineData("202027389", false)]
    [InlineData("20202738931", false)]
    [InlineData("SK20202738", false)]
    public void IsValidDic_ChecksTenDigits(string dic, bool expected) => Assert.Equal(expected, SlovakIdentifiers.IsValidDic(dic));

    [Theory]
    [InlineData("SK2020273893", true)]
    [InlineData("2020273893", false)]
    [InlineData("CZ2020273893", false)]
    [InlineData("SK202027389", false)]
    public void IsValidIcDph_RequiresSkPrefixAndTenDigits(string icDph, bool expected) =>
        Assert.Equal(expected, SlovakIdentifiers.IsValidIcDph(icDph));

    [Fact]
    public void Normalize_RemovesWhitespaceAndUppercases()
    {
        Assert.Equal("SK2020273893", SlovakIdentifiers.Normalize(" sk 2020 273 893 "));
        Assert.Null(SlovakIdentifiers.Normalize("   "));
    }
}
