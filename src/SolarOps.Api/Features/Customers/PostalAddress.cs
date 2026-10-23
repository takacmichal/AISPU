namespace SolarOps.Api.Features.Customers;

/// <summary>
/// Postal address value object (EF owned type). Used by <see cref="Customer"/> and by supply points;
/// other modules may reuse it for their own addresses.
/// </summary>
public sealed record PostalAddress
{
    public const int StreetMaxLength = 200;
    public const int CityMaxLength = 100;
    public const int PostalCodeMaxLength = 10;
    public const string DefaultCountry = "SK";

    // EF Core materialization.
    private PostalAddress()
    {
        Street = City = PostalCode = Country = string.Empty;
    }

    public PostalAddress(string street, string city, string postalCode, string? country = null)
    {
        Street = street.Trim();
        City = city.Trim();
        PostalCode = postalCode.Trim();
        Country = string.IsNullOrWhiteSpace(country) ? DefaultCountry : country.Trim().ToUpperInvariant();
    }

    /// <summary>Street and house number, e.g. "Hlavná 12".</summary>
    public string Street { get; private init; }

    public string City { get; private init; }

    /// <summary>Postal code (PSČ), e.g. "811 01".</summary>
    public string PostalCode { get; private init; }

    /// <summary>ISO 3166-1 alpha-2 country code, default "SK".</summary>
    public string Country { get; private init; }
}
