using SolarOps.Api.Infrastructure.Security;

namespace SolarOps.Api.Tests.Unit.Security;

public sealed class CorsExtensionsTests
{
    [Theory]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("https://app.example.com", "https://app.example.com")]
    [InlineData("https://app.example.com/", "https://app.example.com")]
    [InlineData("https://app.example.com:443", "https://app.example.com")]
    [InlineData("https://APP.example.com", "https://app.example.com")]
    public void NormalizeOrigin_ReturnsSchemeHostAndPort(string configured, string expected)
    {
        Assert.Equal(expected, CorsExtensions.NormalizeOrigin(configured));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("app.example.com")]
    [InlineData("ftp://app.example.com")]
    [InlineData("https://app.example.com/app")]
    [InlineData("https://app.example.com?tenant=1")]
    [InlineData("https://app.example.com#fragment")]
    [InlineData("https://user:pass@app.example.com")]
    public void NormalizeOrigin_RejectsValuesThatAreNotAnOrigin(string configured)
    {
        Assert.Throws<InvalidOperationException>(() => CorsExtensions.NormalizeOrigin(configured));
    }
}
