using SolarOps.Api.Features.Customers;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Tests.Unit.Customers;

public sealed class CustomerDetailsTests
{
    private static readonly PostalAddress Address = new("Hlavná 12", "Bratislava", "811 01");

    private static CustomerDetails Person(
        string? firstName = "Ján", string? lastName = "Novák", string email = "jan@example.sk", string? phone = null,
        string? dic = null, string? icDph = null, string? note = null, PostalAddress? address = null) => new(
        CustomerType.Person, firstName, lastName, null, null, dic, icDph, email, phone, address ?? Address, note);

    private static CustomerDetails Company(string? name = "Slnko s.r.o.", string? ico = "35763469") => new(
        CustomerType.Company, null, null, name, ico, null, null, "info@slnko.sk", null, Address, null);

    private static Dictionary<string, string[]> Errors(CustomerDetails details) => details.Normalize().Validate();

    [Fact]
    public void Valid_Person_HasNoErrors() => Assert.Empty(Errors(Person()));

    [Fact]
    public void Valid_Company_HasNoErrors() => Assert.Empty(Errors(Company()));

    [Fact]
    public void Person_RequiresFirstAndLastName()
    {
        var errors = Errors(Person(firstName: "  ", lastName: null));

        Assert.Contains(nameof(CustomerDetails.FirstName), errors.Keys);
        Assert.Contains(nameof(CustomerDetails.LastName), errors.Keys);
    }

    [Fact]
    public void Company_RequiresNameAndIco()
    {
        var errors = Errors(Company(name: " ", ico: null));

        Assert.Contains(nameof(CustomerDetails.CompanyName), errors.Keys);
        Assert.Contains(nameof(CustomerDetails.Ico), errors.Keys);
    }

    [Fact]
    public void UnknownType_IsRejected()
    {
        var errors = Errors(Person() with { Type = (CustomerType)99 });

        Assert.Contains(nameof(CustomerDetails.Type), errors.Keys);
    }

    [Fact]
    public void Ico_WithWhitespace_IsNormalizedBeforeValidation() =>
        Assert.Empty(Errors(Company(ico: " 35 763 469 ")));

    [Fact]
    public void Ico_WithBadChecksum_IsRejected() =>
        Assert.Contains(nameof(CustomerDetails.Ico), Errors(Company(ico: "35763468")).Keys);

    [Theory]
    [InlineData("202027389")]
    [InlineData("SK2020273893")]
    public void Dic_NotTenDigits_IsRejected(string dic) =>
        Assert.Contains(nameof(CustomerDetails.Dic), Errors(Person(dic: dic)).Keys);

    [Theory]
    [InlineData("2020273893")]
    [InlineData("CZ2020273893")]
    public void IcDph_WithoutSkPrefix_IsRejected(string icDph) =>
        Assert.Contains(nameof(CustomerDetails.IcDph), Errors(Person(icDph: icDph)).Keys);

    [Fact]
    public void IcDph_WithLowerCasePrefixAndSpaces_IsAccepted() =>
        Assert.Empty(Errors(Person(dic: "2020273893", icDph: "sk 2020273893")));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign.sk")]
    [InlineData("a@b")]
    [InlineData("a b@c.sk")]
    [InlineData("a@@c.sk")]
    public void Email_Invalid_IsRejected(string email) =>
        Assert.Contains(nameof(CustomerDetails.Email), Errors(Person(email: email)).Keys);

    [Fact]
    public void Email_LongerThanMax_IsRejected()
    {
        var email = new string('a', CustomerDetails.EmailMaxLength) + "@example.sk";

        Assert.Contains(nameof(CustomerDetails.Email), Errors(Person(email: email)).Keys);
    }

    [Theory]
    [InlineData("+421 900 123 456")]
    [InlineData("0900123456")]
    public void Phone_Valid_IsAccepted(string phone) => Assert.Empty(Errors(Person(phone: phone)));

    [Theory]
    [InlineData("12345")] // too short
    [InlineData("0900-123-456")]
    [InlineData("abc123456")]
    [InlineData("+421+900123456")]
    public void Phone_Invalid_IsRejected(string phone) =>
        Assert.Contains(nameof(CustomerDetails.Phone), Errors(Person(phone: phone)).Keys);

    [Fact]
    public void Note_AtMaxLength_IsValid_AndOneOverIsRejected()
    {
        Assert.Empty(Errors(Person(note: new string('n', CustomerDetails.NoteMaxLength))));
        Assert.Contains(nameof(CustomerDetails.Note), Errors(Person(note: new string('n', CustomerDetails.NoteMaxLength + 1))).Keys);
    }

    [Fact]
    public void Names_LongerThanMax_AreRejected()
    {
        var errors = Errors(Person(firstName: new string('a', CustomerDetails.NameMaxLength + 1), lastName: new string('b', CustomerDetails.NameMaxLength + 1)));

        Assert.Contains(nameof(CustomerDetails.FirstName), errors.Keys);
        Assert.Contains(nameof(CustomerDetails.LastName), errors.Keys);
    }

    [Fact]
    public void CompanyName_LongerThanMax_IsRejected() =>
        Assert.Contains(nameof(CustomerDetails.CompanyName), Errors(Company(name: new string('c', CustomerDetails.CompanyNameMaxLength + 1))).Keys);

    [Fact]
    public void Address_EmptyParts_AreRequired()
    {
        var errors = Errors(Person(address: new PostalAddress("", "", "")));

        Assert.Contains("Address.Street", errors.Keys);
        Assert.Contains("Address.City", errors.Keys);
        Assert.Contains("Address.PostalCode", errors.Keys);
    }

    [Theory]
    [InlineData("8110")]
    [InlineData("811 011")]
    [InlineData("81 101")]
    [InlineData("ABC DE")]
    public void Address_SlovakPostalCode_WrongFormat_IsRejected(string code) =>
        Assert.Contains("Address.PostalCode", Errors(Person(address: new PostalAddress("Hlavná 12", "Bratislava", code))).Keys);

    [Theory]
    [InlineData("811 01")]
    [InlineData("81101")]
    public void Address_SlovakPostalCode_BothFormsAccepted(string code) =>
        Assert.Empty(Errors(Person(address: new PostalAddress("Hlavná 12", "Bratislava", code))));

    [Fact]
    public void Address_ForeignCountry_SkipsSlovakPostalCodeCheck() =>
        Assert.Empty(Errors(Person(address: new PostalAddress("Hlavní 1", "Praha", "110 00", "cz"))));

    [Fact]
    public void Address_InvalidCountryCode_IsRejected() =>
        Assert.Contains("Address.Country", Errors(Person(address: new PostalAddress("Hlavná 12", "Bratislava", "81101", "SVK"))).Keys);

    [Fact]
    public void Address_TooLongStreetAndCity_AreRejected()
    {
        var errors = Errors(Person(address: new PostalAddress(
            new string('s', PostalAddress.StreetMaxLength + 1), new string('c', PostalAddress.CityMaxLength + 1), "81101")));

        Assert.Contains("Address.Street", errors.Keys);
        Assert.Contains("Address.City", errors.Keys);
    }

    [Fact]
    public void Normalize_TrimsTextAndTurnsBlankIntoNull()
    {
        var normalized = Person(firstName: "  Ján ", lastName: " ", email: "  Jan@Example.SK ", phone: " +421 900 123 456 ", note: "  ").Normalize();

        Assert.Equal("Ján", normalized.FirstName);
        Assert.Null(normalized.LastName);
        Assert.Equal("jan@example.sk", normalized.Email);
        Assert.Equal("+421 900 123 456", normalized.Phone);
        Assert.Null(normalized.Note);
    }

    [Theory]
    [InlineData("  Jan@Example.SK ", "jan@example.sk")]
    [InlineData(null, "")]
    public void NormalizeEmail_TrimsAndLowerCases(string? input, string expected) =>
        Assert.Equal(expected, CustomerDetails.NormalizeEmail(input));

    [Fact]
    public void Request_WithoutRequiredMembers_ReportsOnlyRequired()
    {
        var errors = DataAnnotationsValidator.Validate(new CreateCustomerRequest());

        Assert.Contains(nameof(CreateCustomerRequest.Type), errors.Keys);
        Assert.Contains(nameof(CreateCustomerRequest.Email), errors.Keys);
        Assert.Contains(nameof(CreateCustomerRequest.Address), errors.Keys);
    }

    [Fact]
    public void Request_MapsDomainRulesToFieldErrors()
    {
        var request = new CreateCustomerRequest
        {
            Type = CustomerType.Company,
            CompanyName = "Slnko s.r.o.",
            Ico = "12345678",
            Email = "info@slnko.sk",
            Address = new AddressDto { Street = "Hlavná 12", City = "Bratislava", PostalCode = "811 01" },
        };

        var errors = DataAnnotationsValidator.Validate(request);

        Assert.Equal([nameof(CustomerDetails.Ico)], errors.Keys);
    }

    [Fact]
    public void Request_Valid_HasNoErrors()
    {
        var request = new UpdateCustomerRequest
        {
            Type = CustomerType.Person,
            FirstName = "Ján",
            LastName = "Novák",
            Email = "jan@example.sk",
            Address = new AddressDto { Street = "Hlavná 12", City = "Bratislava", PostalCode = "811 01" },
        };

        Assert.Empty(DataAnnotationsValidator.Validate(request));
    }
}
