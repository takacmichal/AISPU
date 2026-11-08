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

/// <summary>Audited operations on a customer (#140).</summary>
public enum CustomerAction
{
    Created,
    Updated,
    Archived,
    Reactivated,
    Anonymized,
}

/// <summary>
/// Customer of the company (person or legal entity). Other modules reference it only by <see cref="Id"/>.
/// The e-mail is stored normalized (lower case) and is unique. <see cref="UserId"/> optionally links the
/// customer to its login account (role Customer), which limits that user to this customer's data.
/// </summary>
public sealed class Customer
{
    /// <summary>Name shown instead of personal data after anonymization.</summary>
    public const string AnonymizedName = "Anonymizovaný zákazník";

    private readonly List<CustomerAuditEntry> _auditEntries = [];

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
    public string DisplayName => AnonymizedAt is not null
        ? AnonymizedName
        : Type == CustomerType.Company
            ? CompanyName ?? string.Empty
            : $"{FirstName} {LastName}".Trim();

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>When the personal data were erased; null while the customer holds personal data.</summary>
    public DateTimeOffset? AnonymizedAt { get; private set; }

    /// <summary>Who, when and what changed; the entries outlive anonymization and contain no personal data.</summary>
    public IReadOnlyList<CustomerAuditEntry> AuditEntries => _auditEntries;

    /// <summary>Creates an active customer. Throws <see cref="RequestValidationException"/> on invalid data.</summary>
    public static Customer Create(CustomerDetails details, Guid? userId, Guid performedBy, DateTimeOffset now)
    {
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            Status = CustomerStatus.Active,
            CreatedAt = now,
            UserId = userId,
        };
        customer.Apply(details, now);
        customer._auditEntries.Add(CustomerAuditEntry.Create(customer.Id, CustomerAction.Created, performedBy, now, "Created"));
        return customer;
    }

    /// <summary>Replaces the editable data. Archived and anonymized customers cannot be edited.</summary>
    public void Update(CustomerDetails details, Guid? userId, Guid performedBy, DateTimeOffset now)
    {
        EnsureActive();
        var normalized = details.Normalize();
        var changedFields = ChangedFields(normalized, userId);
        UserId = userId;
        Apply(normalized, now);
        var detailsText = changedFields.Count == 0 ? "No changes" : string.Join(", ", changedFields);
        _auditEntries.Add(CustomerAuditEntry.Create(Id, CustomerAction.Updated, performedBy, now, detailsText));
    }

    /// <summary>Archives the customer (no new quotes or supply points).</summary>
    public void Archive(Guid performedBy, DateTimeOffset now)
    {
        if (Status == CustomerStatus.Archived)
        {
            throw new DomainException("Customer is already archived.");
        }

        Status = CustomerStatus.Archived;
        UpdatedAt = now;
        _auditEntries.Add(CustomerAuditEntry.Create(Id, CustomerAction.Archived, performedBy, now, "Status Active -> Archived"));
    }

    /// <summary>Returns an archived customer back to the active state. An anonymized customer cannot be reactivated.</summary>
    public void Reactivate(Guid performedBy, DateTimeOffset now)
    {
        if (AnonymizedAt is not null)
        {
            throw new DomainException("An anonymized customer cannot be reactivated.");
        }

        if (Status == CustomerStatus.Active)
        {
            throw new DomainException("Customer is already active.");
        }

        Status = CustomerStatus.Active;
        UpdatedAt = now;
        _auditEntries.Add(CustomerAuditEntry.Create(Id, CustomerAction.Reactivated, performedBy, now, "Status Archived -> Active"));
    }

    /// <summary>
    /// Erases the personal data (GDPR right to erasure, #140): names, identifiers, contact details, address, note and
    /// the login link are cleared and the customer is archived. The id, timestamps and audit trail stay, so quotes,
    /// contracts and installations keep their references.
    /// </summary>
    public void Anonymize(Guid performedBy, DateTimeOffset now)
    {
        if (AnonymizedAt is not null)
        {
            throw new DomainException("Customer is already anonymized.");
        }

        FirstName = null;
        LastName = null;
        CompanyName = null;
        Ico = null;
        Dic = null;
        IcDph = null;
        Email = $"anonymized-{Id:N}@anonymized.invalid";
        Phone = null;
        Address = new PostalAddress(string.Empty, string.Empty, string.Empty);
        Note = null;
        UserId = null;
        SortName = AnonymizedName;
        Status = CustomerStatus.Archived;
        AnonymizedAt = now;
        UpdatedAt = now;
        _auditEntries.Add(CustomerAuditEntry.Create(Id, CustomerAction.Anonymized, performedBy, now, "Personal data erased; customer archived"));
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

    /// <summary>
    /// Names of the fields whose value would change. Only field names are audited, never the values: the audit trail
    /// must not keep personal data after anonymization (#140).
    /// </summary>
    private List<string> ChangedFields(CustomerDetails normalized, Guid? userId)
    {
        var changed = new List<string>();
        if (Type != normalized.Type)
        {
            changed.Add(nameof(Type));
        }

        if (FirstName != normalized.FirstName)
        {
            changed.Add(nameof(FirstName));
        }

        if (LastName != normalized.LastName)
        {
            changed.Add(nameof(LastName));
        }

        if (CompanyName != normalized.CompanyName)
        {
            changed.Add(nameof(CompanyName));
        }

        if (Ico != normalized.Ico)
        {
            changed.Add(nameof(Ico));
        }

        if (Dic != normalized.Dic)
        {
            changed.Add(nameof(Dic));
        }

        if (IcDph != normalized.IcDph)
        {
            changed.Add(nameof(IcDph));
        }

        if (Email != normalized.Email)
        {
            changed.Add(nameof(Email));
        }

        if (Phone != normalized.Phone)
        {
            changed.Add(nameof(Phone));
        }

        if (Address != normalized.Address)
        {
            changed.Add(nameof(Address));
        }

        if (Note != normalized.Note)
        {
            changed.Add(nameof(Note));
        }

        if (UserId != userId)
        {
            changed.Add(nameof(UserId));
        }

        return changed;
    }
}

/// <summary>Audit trail entry of a customer: who did what and when. It never stores personal data values.</summary>
public sealed class CustomerAuditEntry
{
    public const int DetailsMaxLength = 500;

    private CustomerAuditEntry()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public CustomerAction Action { get; private set; }

    public Guid PerformedBy { get; private set; }

    public DateTimeOffset PerformedAt { get; private set; }

    public string? Details { get; private set; }

    internal static CustomerAuditEntry Create(Guid customerId, CustomerAction action, Guid by, DateTimeOffset at, string? details) => new()
    {
        Id = Guid.CreateVersion7(),
        CustomerId = customerId,
        Action = action,
        PerformedBy = by,
        PerformedAt = at,
        Details = details,
    };
}
