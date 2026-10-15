using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Customers;

/// <summary>Legal form of a customer.</summary>
public enum CustomerType
{
    /// <summary>Natural person (fyzická osoba, FO).</summary>
    Person,

    /// <summary>Legal entity (právnická osoba, PO); IČO is required.</summary>
    Company,
}

/// <summary>Lifecycle state of a customer (BPMN mapping: Active, Archived).</summary>
public enum CustomerStatus
{
    Active,
    Archived,
}

/// <summary>
/// Customer of the company (person or legal entity). Other modules reference it only by <see cref="Id"/>.
/// The e-mail is stored normalized (lower case) and is unique. <see cref="UserId"/> optionally links the
/// customer to its login account (role Customer), which limits that user to this customer's data.
/// </summary>
public sealed class Customer
{
    // EF Core materialization.
    private Customer()
    {
        Email = SortName = string.Empty;
        Address = null!;
    }

    public Guid Id { get; private set; }

    public CustomerType Type { get; private set; }

    public CustomerStatus Status { get; private set; }

    /// <summary>First name (persons only).</summary>
    public string? FirstName { get; private set; }

    /// <summary>Last name (persons only).</summary>
    public string? LastName { get; private set; }

    /// <summary>Business name (companies only).</summary>
    public string? CompanyName { get; private set; }

    /// <summary>IČO, 8 digits with a check digit (required for companies).</summary>
    public string? Ico { get; private set; }

    /// <summary>DIČ, 10 digits.</summary>
    public string? Dic { get; private set; }

    /// <summary>IČ DPH, "SK" + 10 digits.</summary>
    public string? IcDph { get; private set; }

    /// <summary>Contact e-mail, normalized to lower case, unique.</summary>
    public string Email { get; private set; }

    public string? Phone { get; private set; }

    /// <summary>Billing / correspondence address.</summary>
    public PostalAddress Address { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Login account of the customer (role Customer), at most one customer per account.</summary>
    public Guid? UserId { get; private set; }

    /// <summary>
    /// Persisted sort key: "Last First" for persons, company name for companies.
    /// Lists are ordered by it by default ("by surname").
    /// </summary>
    public string SortName { get; private set; }

    /// <summary>Human readable name: "First Last" for persons, company name for companies.</summary>
    public string DisplayName => Type == CustomerType.Company
        ? CompanyName ?? string.Empty
        : $"{FirstName} {LastName}".Trim();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Creates an active customer. Throws <see cref="RequestValidationException"/> on invalid data.</summary>
    public static Customer Create(CustomerDetails details, Guid? userId, DateTimeOffset now)
    {
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            Status = CustomerStatus.Active,
            CreatedAt = now,
            UserId = userId,
        };
        customer.Apply(details, now);
        return customer;
    }

    /// <summary>Replaces the editable data. Archived customers cannot be edited.</summary>
    public void Update(CustomerDetails details, Guid? userId, DateTimeOffset now)
    {
        EnsureActive();
        UserId = userId;
        Apply(details, now);
    }

    /// <summary>Archives the customer (no new quotes or supply points).</summary>
    public void Archive(DateTimeOffset now)
    {
        if (Status == CustomerStatus.Archived)
        {
            throw new DomainException("Customer is already archived.");
        }

        Status = CustomerStatus.Archived;
        UpdatedAt = now;
    }

    /// <summary>Returns an archived customer back to the active state.</summary>
    public void Reactivate(DateTimeOffset now)
    {
        if (Status == CustomerStatus.Active)
        {
            throw new DomainException("Customer is already active.");
        }

        Status = CustomerStatus.Active;
        UpdatedAt = now;
    }

    /// <summary>Throws <see cref="DomainException"/> (409) when the customer is archived.</summary>
    public void EnsureActive()
    {
        if (Status != CustomerStatus.Active)
        {
            throw new DomainException("Customer is archived.");
        }
    }

    private void Apply(CustomerDetails details, DateTimeOffset now)
    {
        var normalized = details.Normalize();
        var errors = normalized.Validate();
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        Type = normalized.Type;
        // A person has no business name; a company has no personal names.
        FirstName = normalized.Type == CustomerType.Person ? normalized.FirstName : null;
        LastName = normalized.Type == CustomerType.Person ? normalized.LastName : null;
        CompanyName = normalized.Type == CustomerType.Company ? normalized.CompanyName : null;
        Ico = normalized.Ico;
        Dic = normalized.Dic;
        IcDph = normalized.IcDph;
        Email = normalized.Email;
        Phone = normalized.Phone;
        Address = normalized.Address;
        Note = normalized.Note;
        SortName = Type == CustomerType.Company
            ? CompanyName!
            : $"{LastName} {FirstName}";
        UpdatedAt = now;
    }
}
