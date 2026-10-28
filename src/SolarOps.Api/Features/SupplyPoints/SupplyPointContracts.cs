using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using SolarOps.Api.Features.Customers;

namespace SolarOps.Api.Features.SupplyPoints;

/// <summary>Payload for creating or replacing a supply point.</summary>
public abstract partial record SupplyPointRequest : IValidatableObject
{
    /// <summary>Optional label, e.g. "Rodinný dom".</summary>
    [MaxLength(SupplyPoint.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>EIC code of the metering point (16 characters, e.g. "24ZSS1234567890A"); whitespace is ignored.</summary>
    [Required]
    public string? EicCode { get; init; }

    [Required]
    public AddressDto? Address { get; init; }

    /// <summary>Rated current of the main circuit breaker in amperes (1-1000).</summary>
    [Required, Range(SupplyPoint.MinBreakerAmps, SupplyPoint.MaxBreakerAmps)]
    public int? MainBreakerAmps { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var eic = SupplyPoint.NormalizeEic(EicCode);
        if (eic is not null && !SupplyPoint.IsValidEic(eic))
        {
            yield return new ValidationResult(
                "EIC must have 16 characters: 2 digits, a letter, 12 letters/digits/'-' and a check character.", [nameof(EicCode)]);
        }

        if (Address is null)
        {
            yield break;
        }

        var address = Address.ToDomain();
        foreach (var (field, value, max) in new[]
        {
            ("Address.Street", address.Street, PostalAddress.StreetMaxLength),
            ("Address.City", address.City, PostalAddress.CityMaxLength),
            ("Address.PostalCode", address.PostalCode, PostalAddress.PostalCodeMaxLength),
        })
        {
            if (value.Length == 0)
            {
                yield return new ValidationResult("Value is required.", [field]);
            }
            else if (value.Length > max)
            {
                yield return new ValidationResult($"Maximum length is {max} characters.", [field]);
            }
        }

        if (address.Country == PostalAddress.DefaultCountry && address.PostalCode.Length > 0 && !SkPostalCodeRegex().IsMatch(address.PostalCode))
        {
            yield return new ValidationResult("Slovak postal code must have 5 digits, e.g. '811 01'.", ["Address.PostalCode"]);
        }

        if (!CountryRegex().IsMatch(address.Country))
        {
            yield return new ValidationResult("Country must be a 2-letter ISO code.", ["Address.Country"]);
        }
    }

    [GeneratedRegex(@"^\d{3} ?\d{2}$")]
    private static partial Regex SkPostalCodeRegex();

    [GeneratedRegex(@"^[A-Z]{2}$")]
    private static partial Regex CountryRegex();
}

public sealed record CreateSupplyPointRequest : SupplyPointRequest;

public sealed record UpdateSupplyPointRequest : SupplyPointRequest;

/// <summary>Supply point returned by the API.</summary>
public sealed record SupplyPointResponse(
    Guid Id,
    Guid CustomerId,
    string? Name,
    string EicCode,
    AddressDto Address,
    int MainBreakerAmps,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static SupplyPointResponse FromEntity(SupplyPoint x) => new(
        x.Id, x.CustomerId, x.Name, x.EicCode, AddressDto.FromDomain(x.Address), x.MainBreakerAmps, x.CreatedAt, x.UpdatedAt);
}
