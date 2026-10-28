using SolarOps.Api.Features.Customers;

namespace SolarOps.Api.Tests.Unit.Customers;

public sealed class PostalAddressTests
{
    [Fact]
    public void Constructor_TrimsPartsAndDefaultsCountryToSk()
    {
        var address = new PostalAddress("  Hlavná 12 ", " Bratislava ", " 811 01 ");

        Assert.Equal("Hlavná 12", address.Street);
        Assert.Equal("Bratislava", address.City);
        Assert.Equal("811 01", address.PostalCode);
        Assert.Equal(PostalAddress.DefaultCountry, address.Country);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Constructor_BlankCountry_FallsBackToSk(string? country) =>
        Assert.Equal("SK", new PostalAddress("a", "b", "c", country).Country);

    [Fact]
    public void Constructor_UpperCasesAndTrimsCountry() =>
        Assert.Equal("CZ", new PostalAddress("a", "b", "c", " cz ").Country);

    [Fact]
    public void Addresses_WithSameValues_AreEqual() =>
        Assert.Equal(new PostalAddress("a", "b", "c"), new PostalAddress(" a", "b ", "c", "sk"));

    [Fact]
    public void AddressDto_ToDomain_MissingPartsBecomeEmpty()
    {
        var address = new AddressDto().ToDomain();

        Assert.Equal(string.Empty, address.Street);
        Assert.Equal(string.Empty, address.City);
        Assert.Equal("SK", address.Country);
    }

    [Fact]
    public void AddressDto_RoundTripsThroughDomain()
    {
        var address = new PostalAddress("Hlavná 12", "Bratislava", "811 01", "SK");

        Assert.Equal(address, AddressDto.FromDomain(address).ToDomain());
    }
}
