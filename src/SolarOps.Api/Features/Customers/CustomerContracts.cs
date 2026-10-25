using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Customers;

/// <summary>Postal address in requests and responses.</summary>
public sealed record AddressDto
{
    /// <summary>Street and house number, e.g. "Hlavná 12".</summary>
    public string? Street { get; init; }

    public string? City { get; init; }

    /// <summary>Postal code (PSČ), e.g. "811 01".</summary>
    public string? PostalCode { get; init; }

    /// <summary>ISO 3166-1 alpha-2 country code; defaults to "SK".</summary>
    public string? Country { get; init; }

    public PostalAddress ToDomain() => new(Street ?? string.Empty, City ?? string.Empty, PostalCode ?? string.Empty, Country);

    public static AddressDto FromDomain(PostalAddress address) => new()
    {
        Street = address.Street,
        City = address.City,
        PostalCode = address.PostalCode,
        Country = address.Country,
    };
}

/// <summary>
/// Customer data sent when creating or updating a customer. Persons need first and last name,
/// companies need company name and IČO (validated with its check digit).
/// </summary>
public abstract record CustomerRequest : IValidatableObject
{
    /// <summary>Person (FO) or Company (PO).</summary>
    [Required]
    public CustomerType? Type { get; init; }

    public string? FirstName { get; init; }

    public string? LastName { get; init; }

    public string? CompanyName { get; init; }

    /// <summary>IČO, 8 digits; required for companies.</summary>
    public string? Ico { get; init; }

    /// <summary>DIČ, 10 digits.</summary>
    public string? Dic { get; init; }

    /// <summary>IČ DPH, "SK" + 10 digits.</summary>
    public string? IcDph { get; init; }

    /// <summary>Contact e-mail; must be unique among customers.</summary>
    [Required]
    public string? Email { get; init; }

    public string? Phone { get; init; }

    [Required]
    public AddressDto? Address { get; init; }

    public string? Note { get; init; }

    /// <summary>Optional login account (user with role Customer) linked to this customer.</summary>
    public Guid? UserId { get; init; }

    public CustomerDetails ToDetails() => new(
        Type ?? CustomerType.Person,
        FirstName,
        LastName,
        CompanyName,
        Ico,
        Dic,
        IcDph,
        Email ?? string.Empty,
        Phone,
        (Address ?? new AddressDto()).ToDomain(),
        Note);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already reports missing members; the domain rules need them to be present.
        if (Type is null || Email is null || Address is null)
        {
            yield break;
        }

        foreach (var (field, messages) in ToDetails().Normalize().Validate())
        {
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [field]);
            }
        }
    }
}

/// <summary>Payload for <c>POST /api/customers</c>.</summary>
public sealed record CreateCustomerRequest : CustomerRequest;

/// <summary>Payload for <c>PUT /api/customers/{id}</c> (full replacement of the editable data).</summary>
public sealed record UpdateCustomerRequest : CustomerRequest;

/// <summary>Page of customers returned by <c>GET /api/customers</c>.</summary>
public sealed record CustomerListResponse(
    IReadOnlyList<CustomerResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

/// <summary>Customer returned by the API.</summary>
public sealed record CustomerResponse(
    Guid Id,
    CustomerType Type,
    CustomerStatus Status,
    string DisplayName,
    string? FirstName,
    string? LastName,
    string? CompanyName,
    string? Ico,
    string? Dic,
    string? IcDph,
    string Email,
    string? Phone,
    AddressDto Address,
    string? Note,
    Guid? UserId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static CustomerResponse FromEntity(Customer c) => new(
        c.Id,
        c.Type,
        c.Status,
        c.DisplayName,
        c.FirstName,
        c.LastName,
        c.CompanyName,
        c.Ico,
        c.Dic,
        c.IcDph,
        c.Email,
        c.Phone,
        AddressDto.FromDomain(c.Address),
        c.Note,
        c.UserId,
        c.CreatedAt,
        c.UpdatedAt);
}
