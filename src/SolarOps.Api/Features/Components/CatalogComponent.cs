using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Components;

public enum ComponentType
{
    Panel,
    Inverter,
    Labor,
}

/// <summary>
/// Priced item of the catalog (panel, inverter or labor). Quotes (#74) must copy the unit price when they
/// are issued, so a later price change here does not alter issued quotes. Inactive components are not offered.
/// </summary>
public sealed class CatalogComponent
{
    public const int CodeMaxLength = 50;
    public const int NameMaxLength = 200;
    public const int ManufacturerMaxLength = 100;
    public const decimal MaxUnitPrice = 1_000_000m;

    private CatalogComponent()
    {
        Code = Name = string.Empty;
    }

    public Guid Id { get; private set; }

    public ComponentType Type { get; private set; }

    /// <summary>Catalog code (SKU), stored upper case and unique.</summary>
    public string Code { get; private set; }

    public string Name { get; private set; }

    public string? Manufacturer { get; private set; }

    /// <summary>Rated power in watts; required for panels and inverters.</summary>
    public int? PowerW { get; private set; }

    /// <summary>Price per unit in EUR (per piece, per hour for labor), two decimals.</summary>
    public decimal UnitPrice { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    public static CatalogComponent Create(ComponentDetails details, DateTimeOffset now)
    {
        var component = new CatalogComponent
        {
            Id = Guid.CreateVersion7(),
            IsActive = true,
            CreatedAt = now,
        };
        component.Apply(details, now);
        return component;
    }

    public void Update(ComponentDetails details, DateTimeOffset now) => Apply(details, now);

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            throw new DomainException("Component is already inactive.");
        }

        IsActive = false;
        UpdatedAt = now;
    }

    public void Activate(DateTimeOffset now)
    {
        if (IsActive)
        {
            throw new DomainException("Component is already active.");
        }

        IsActive = true;
        UpdatedAt = now;
    }

    /// <summary>Throws <see cref="DomainException"/> (409) when the component must not be offered.</summary>
    public void EnsureActive()
    {
        if (!IsActive)
        {
            throw new DomainException($"Component '{Code}' is inactive.");
        }
    }

    private void Apply(ComponentDetails details, DateTimeOffset now)
    {
        var errors = details.Validate();
        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        Type = details.Type;
        Code = NormalizeCode(details.Code);
        Name = details.Name.Trim();
        Manufacturer = string.IsNullOrWhiteSpace(details.Manufacturer) ? null : details.Manufacturer.Trim();
        PowerW = details.PowerW;
        UnitPrice = details.UnitPrice;
        UpdatedAt = now;
    }
}

/// <summary>Editable data of a component.</summary>
public sealed record ComponentDetails(
    ComponentType Type,
    string Code,
    string Name,
    string? Manufacturer,
    int? PowerW,
    decimal UnitPrice)
{
    public IDictionary<string, string[]> Validate()
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(Code) || Code.Trim().Length > CatalogComponent.CodeMaxLength)
        {
            errors["Code"] = [$"Code is required and at most {CatalogComponent.CodeMaxLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Trim().Length > CatalogComponent.NameMaxLength)
        {
            errors["Name"] = [$"Name is required and at most {CatalogComponent.NameMaxLength} characters."];
        }

        if (Manufacturer?.Trim().Length > CatalogComponent.ManufacturerMaxLength)
        {
            errors["Manufacturer"] = [$"Manufacturer is at most {CatalogComponent.ManufacturerMaxLength} characters."];
        }

        if (Type is ComponentType.Panel or ComponentType.Inverter)
        {
            if (PowerW is null or <= 0)
            {
                errors["PowerW"] = ["Power in watts (greater than 0) is required for panels and inverters."];
            }
        }
        else if (PowerW is not null)
        {
            errors["PowerW"] = ["Labor has no power."];
        }

        if (UnitPrice is < 0 or > CatalogComponent.MaxUnitPrice)
        {
            errors["UnitPrice"] = [$"Unit price must be between 0 and {CatalogComponent.MaxUnitPrice:0}."];
        }
        else if (decimal.Round(UnitPrice, 2) != UnitPrice)
        {
            errors["UnitPrice"] = ["Unit price has at most two decimal places."];
        }

        return errors;
    }
}
