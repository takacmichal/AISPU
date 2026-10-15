using System.Text.RegularExpressions;

namespace SolarOps.Api.Features.Customers;

/// <summary>
/// Editable data of a <see cref="Customer"/>. <see cref="Normalize"/> and <see cref="Validate"/> are the single
/// source of the customer rules, used both by request DTOs (400 before the handler runs) and by the entity.
/// </summary>
public sealed partial record CustomerDetails(
    CustomerType Type,
    string? FirstName,
    string? LastName,
    string? CompanyName,
    string? Ico,
    string? Dic,
    string? IcDph,
    string Email,
    string? Phone,
    PostalAddress Address,
    string? Note)
{
    public const int NameMaxLength = 100;
    public const int CompanyNameMaxLength = 200;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 20;
    public const int NoteMaxLength = 2000;

    /// <summary>Trims text, lower-cases the e-mail and strips whitespace from identifiers.</summary>
    public CustomerDetails Normalize() => this with
    {
        FirstName = Clean(FirstName),
        LastName = Clean(LastName),
        CompanyName = Clean(CompanyName),
        Ico = SlovakIdentifiers.Normalize(Ico),
        Dic = SlovakIdentifiers.Normalize(Dic),
        IcDph = SlovakIdentifiers.Normalize(IcDph),
        Email = NormalizeEmail(Email),
        Phone = Clean(Phone),
        Address = new PostalAddress(Address.Street, Address.City, Address.PostalCode, Address.Country),
        Note = Clean(Note),
    };

    /// <summary>Normalized form of an e-mail address (trimmed, lower case), used for uniqueness.</summary>
    public static string NormalizeEmail(string? email) => (email ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary>Validates a normalized instance; returns errors grouped by field (empty when valid).</summary>
    public Dictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        void Add(string field, string message)
        {
            if (!errors.TryGetValue(field, out var list))
            {
                errors[field] = list = [];
            }

            list.Add(message);
        }

        if (!Enum.IsDefined(Type))
        {
            Add(nameof(Type), "Unknown customer type.");
        }

        if (Type == CustomerType.Person)
        {
            if (FirstName is null)
            {
                Add(nameof(FirstName), "First name is required for a person.");
            }

            if (LastName is null)
            {
                Add(nameof(LastName), "Last name is required for a person.");
            }
        }

        if (Type == CustomerType.Company)
        {
            if (CompanyName is null)
            {
                Add(nameof(CompanyName), "Company name is required for a company.");
            }

            if (Ico is null)
            {
                Add(nameof(Ico), "IČO is required for a company.");
            }
        }

        MaxLength(nameof(FirstName), FirstName, NameMaxLength);
        MaxLength(nameof(LastName), LastName, NameMaxLength);
        MaxLength(nameof(CompanyName), CompanyName, CompanyNameMaxLength);
        MaxLength(nameof(Phone), Phone, PhoneMaxLength);
        MaxLength(nameof(Note), Note, NoteMaxLength);

        if (Ico is not null && !SlovakIdentifiers.IsValidIco(Ico))
        {
            Add(nameof(Ico), "IČO must have 8 digits with a valid check digit.");
        }

        if (Dic is not null && !SlovakIdentifiers.IsValidDic(Dic))
        {
            Add(nameof(Dic), "DIČ must have 10 digits.");
        }

        if (IcDph is not null && !SlovakIdentifiers.IsValidIcDph(IcDph))
        {
            Add(nameof(IcDph), "IČ DPH must be 'SK' followed by 10 digits.");
        }

        if (Email.Length == 0)
        {
            Add(nameof(Email), "E-mail is required.");
        }
        else if (Email.Length > EmailMaxLength || !EmailRegex().IsMatch(Email))
        {
            Add(nameof(Email), "E-mail is not a valid address.");
        }

        if (Phone is not null && !PhoneRegex().IsMatch(Phone))
        {
            Add(nameof(Phone), "Phone may contain only digits, spaces and a leading '+'.");
        }

        Required("Address.Street", Address.Street, PostalAddress.StreetMaxLength);
        Required("Address.City", Address.City, PostalAddress.CityMaxLength);
        Required("Address.PostalCode", Address.PostalCode, PostalAddress.PostalCodeMaxLength);
        if (Address.Country == PostalAddress.DefaultCountry && Address.PostalCode.Length > 0 && !SkPostalCodeRegex().IsMatch(Address.PostalCode))
        {
            Add("Address.PostalCode", "Slovak postal code must have 5 digits, e.g. '811 01'.");
        }

        if (!CountryRegex().IsMatch(Address.Country))
        {
            Add("Address.Country", "Country must be a 2-letter ISO code.");
        }

        return errors.ToDictionary(e => e.Key, e => e.Value.ToArray(), StringComparer.Ordinal);

        void MaxLength(string field, string? value, int max)
        {
            if (value is not null && value.Length > max)
            {
                Add(field, $"Maximum length is {max} characters.");
            }
        }

        void Required(string field, string value, int max)
        {
            if (value.Length == 0)
            {
                Add(field, "Value is required.");
            }
            else
            {
                MaxLength(field, value, max);
            }
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"^\+?[0-9 ]{6,19}$")]
    private static partial Regex PhoneRegex();

    [GeneratedRegex(@"^\d{3} ?\d{2}$")]
    private static partial Regex SkPostalCodeRegex();

    [GeneratedRegex(@"^[A-Z]{2}$")]
    private static partial Regex CountryRegex();
}
