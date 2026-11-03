using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.SupplyPoints;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.SupplyPoints;

public sealed class SupplyPointTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 8, 0, 0, TimeSpan.Zero);

    private static readonly AddressDto ValidAddress = new() { Street = "Hlavná 12", City = "Bratislava", PostalCode = "811 01" };

    private static CreateSupplyPointRequest Request(
        string? eic = "24ZSS1234567890A", AddressDto? address = null, int? amps = 25) => new()
        {
            Name = "Rodinný dom",
            EicCode = eic,
            Address = address ?? ValidAddress,
            MainBreakerAmps = amps,
        };

    [Theory]
    [InlineData("24ZSS1234567890A")]
    [InlineData("24ZSS1234567890X")]
    [InlineData("24Z-S-1234567-0A")]
    [InlineData("00ZABCDEFGHIJKL9")]
    public void IsValidEic_AcceptsWellFormedCodes(string eic) => Assert.True(SupplyPoint.IsValidEic(eic));

    [Theory]
    [InlineData("")]
    [InlineData("24ZSS1234567890")] // 15 characters
    [InlineData("24ZSS1234567890AB")] // 17 characters
    [InlineData("A4ZSS1234567890A")] // office must be 2 digits
    [InlineData("242SS1234567890A")] // object type must be a letter
    [InlineData("24zss1234567890a")] // not normalized (lower case)
    [InlineData("24ZSS1234567890-")] // check character cannot be '-'
    [InlineData("24ZSS 234567890A")] // whitespace is removed by NormalizeEic first
    public void IsValidEic_RejectsMalformedCodes(string eic) => Assert.False(SupplyPoint.IsValidEic(eic));

    [Fact]
    public void NormalizeEic_RemovesWhitespaceAndUppercases()
    {
        Assert.Equal("24ZSS1234567890A", SupplyPoint.NormalizeEic(" 24zss 1234 5678 90a\t"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeEic_BlankIsNull(string? value) => Assert.Null(SupplyPoint.NormalizeEic(value));

    [Fact]
    public void Create_SetsFieldsAndTimestamps()
    {
        var customerId = Guid.CreateVersion7();
        var address = new PostalAddress("Hlavná 12", "Bratislava", "811 01");

        var supplyPoint = SupplyPoint.Create(customerId, "Dom", "24ZSS1234567890A", address, 25, Now);

        Assert.NotEqual(Guid.Empty, supplyPoint.Id);
        Assert.Equal(customerId, supplyPoint.CustomerId);
        Assert.Equal("24ZSS1234567890A", supplyPoint.EicCode);
        Assert.Equal(25, supplyPoint.MainBreakerAmps);
        Assert.Equal(Now, supplyPoint.CreatedAt);
        Assert.Equal(Now, supplyPoint.UpdatedAt);
    }

    [Fact]
    public void Update_ReplacesDataAndOnlyTouchesUpdatedAt()
    {
        var supplyPoint = SupplyPoint.Create(Guid.CreateVersion7(), null, "24ZSS1234567890A", new PostalAddress("A 1", "B", "81101"), 25, Now);
        var newAddress = new PostalAddress("Nová 5", "Košice", "040 01");

        supplyPoint.Update("Chata", "24ZSS9999999999B", newAddress, 40, Now.AddDays(1));

        Assert.Equal("Chata", supplyPoint.Name);
        Assert.Equal("24ZSS9999999999B", supplyPoint.EicCode);
        Assert.Equal(newAddress, supplyPoint.Address);
        Assert.Equal(40, supplyPoint.MainBreakerAmps);
        Assert.Equal(Now, supplyPoint.CreatedAt);
        Assert.Equal(Now.AddDays(1), supplyPoint.UpdatedAt);
    }

    [Fact]
    public void Request_Valid_HasNoErrors() => Assert.Empty(DataAnnotationsValidator.Validate(Request()));

    [Fact]
    public void Request_EicWithSpacesAndLowerCase_IsAccepted() =>
        Assert.Empty(DataAnnotationsValidator.Validate(Request(eic: " 24zss 1234567890a ")));

    [Fact]
    public void Request_MalformedEic_IsRejected()
    {
        var errors = DataAnnotationsValidator.Validate(Request(eic: "NOT-AN-EIC"));

        Assert.Contains(nameof(CreateSupplyPointRequest.EicCode), errors.Keys);
    }

    [Fact]
    public void Request_MissingEic_ReportsRequiredOnly()
    {
        var errors = DataAnnotationsValidator.Validate(Request(eic: null));

        Assert.Contains(nameof(CreateSupplyPointRequest.EicCode), errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1001)]
    [InlineData(null)]
    public void Request_BreakerOutOfRange_IsRejected(int? amps)
    {
        var errors = DataAnnotationsValidator.Validate(Request(amps: amps));

        Assert.Contains(nameof(CreateSupplyPointRequest.MainBreakerAmps), errors.Keys);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1000)]
    public void Request_BreakerOnBoundary_IsValid(int amps) =>
        Assert.Empty(DataAnnotationsValidator.Validate(Request(amps: amps)));

    [Fact]
    public void Request_MissingAddress_IsRejected()
    {
        var errors = DataAnnotationsValidator.Validate(Request(address: null) with { Address = null });

        Assert.Contains(nameof(CreateSupplyPointRequest.Address), errors.Keys);
    }

    [Fact]
    public void Request_EmptyAddressParts_AreRequired()
    {
        var errors = DataAnnotationsValidator.Validate(Request(address: new AddressDto()));

        Assert.Contains("Address.Street", errors.Keys);
        Assert.Contains("Address.City", errors.Keys);
        Assert.Contains("Address.PostalCode", errors.Keys);
    }

    [Fact]
    public void Request_SlovakPostalCodeWithWrongFormat_IsRejected()
    {
        var address = ValidAddress with { PostalCode = "8110" };

        Assert.Contains("Address.PostalCode", DataAnnotationsValidator.Validate(Request(address: address)).Keys);
    }

    [Fact]
    public void Request_ForeignPostalCode_IsNotCheckedAgainstSlovakFormat()
    {
        var address = ValidAddress with { PostalCode = "110 00", Country = "cz" };

        Assert.Empty(DataAnnotationsValidator.Validate(Request(address: address)));
    }

    [Fact]
    public void Request_TooLongStreet_IsRejected()
    {
        var address = ValidAddress with { Street = new string('x', PostalAddress.StreetMaxLength + 1) };

        Assert.Contains("Address.Street", DataAnnotationsValidator.Validate(Request(address: address)).Keys);
    }

    [Fact]
    public void Request_InvalidCountry_IsRejected()
    {
        var address = ValidAddress with { Country = "SVK" };

        Assert.Contains("Address.Country", DataAnnotationsValidator.Validate(Request(address: address)).Keys);
    }
}
