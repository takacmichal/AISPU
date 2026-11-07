using SolarOps.Api.Infrastructure.Errors;
using SolarOps.Api.Infrastructure.Querying;

namespace SolarOps.Api.Tests.Unit.Querying;

public sealed class EnumFilterTests
{
    public enum Color
    {
        Red,
        Green,
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void BlankValue_MeansNoFilter(string? value) => Assert.Null(EnumFilter.Parse<Color>(value, "color"));

    [Theory]
    [InlineData("Green", Color.Green)]
    [InlineData("red", Color.Red)]
    [InlineData(" GREEN ", Color.Green)]
    public void KnownName_IsParsedCaseInsensitively(string value, Color expected) =>
        Assert.Equal(expected, EnumFilter.Parse<Color>(value, "color"));

    [Theory]
    [InlineData("Blue")]
    [InlineData("99")]
    [InlineData("-1")]
    public void UnknownNameOrUndefinedNumber_ThrowsOnTheField(string value)
    {
        var ex = Assert.Throws<RequestValidationException>(() => EnumFilter.Parse<Color>(value, "color"));

        var message = Assert.Single(ex.Errors["color"]);
        Assert.Contains("Red", message);
        Assert.Contains("Green", message);
    }
}
