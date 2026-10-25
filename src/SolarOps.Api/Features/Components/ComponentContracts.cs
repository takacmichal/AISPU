using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Components;

/// <summary>Component data sent when creating or updating a component.</summary>
public abstract record ComponentRequest : IValidatableObject
{
    [Required]
    public ComponentType? Type { get; init; }

    /// <summary>Catalog code (SKU); unique, case-insensitive.</summary>
    [Required]
    public string? Code { get; init; }

    [Required]
    public string? Name { get; init; }

    public string? Manufacturer { get; init; }

    /// <summary>Rated power in watts; required for panels and inverters.</summary>
    public int? PowerW { get; init; }

    /// <summary>Price per unit in EUR (per hour for labor), at most two decimals.</summary>
    [Required]
    public decimal? UnitPrice { get; init; }

    public ComponentDetails ToDetails() =>
        new(Type ?? ComponentType.Panel, Code ?? string.Empty, Name ?? string.Empty, Manufacturer, PowerW, UnitPrice ?? 0m);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // [Required] already reports missing members.
        if (Type is null || Code is null || Name is null || UnitPrice is null)
        {
            yield break;
        }

        foreach (var (field, messages) in ToDetails().Validate())
        {
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [field]);
            }
        }
    }
}

public sealed record CreateComponentRequest : ComponentRequest;

public sealed record UpdateComponentRequest : ComponentRequest;

public sealed record ComponentResponse(
    Guid Id,
    ComponentType Type,
    string Code,
    string Name,
    string? Manufacturer,
    int? PowerW,
    decimal UnitPrice,
    string Currency,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static ComponentResponse FromEntity(CatalogComponent c) => new(
        c.Id,
        c.Type,
        c.Code,
        c.Name,
        c.Manufacturer,
        c.PowerW,
        c.UnitPrice,
        "EUR",
        c.IsActive,
        c.CreatedAt,
        c.UpdatedAt);
}
