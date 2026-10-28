using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Installations;

/// <summary>Creates or updates a string (shared body).</summary>
public record StringRequest : IValidatableObject
{
    /// <summary>String number as the inverter reports it in telemetry (<c>strings[].id</c>); unique within the installation.</summary>
    [Required, Range(1, PanelString.MaxNumber)]
    public int? Number { get; init; }

    [MaxLength(PanelString.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>MPPT input of the inverter the string is wired to.</summary>
    [Range(1, PanelString.MaxNumber)]
    public int? MpptInput { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is { Length: > 0 } && Name.Trim().Length > PanelString.NameMaxLength)
        {
            yield return new ValidationResult($"Name is at most {PanelString.NameMaxLength} characters.", [nameof(Name)]);
        }
    }
}

public sealed record CreateStringRequest : StringRequest;

public sealed record UpdateStringRequest : StringRequest;

/// <summary>A panel placed in a string.</summary>
public sealed record StringPanelResponse(Guid Id, string SerialNumber, string Manufacturer, string Model, int PowerW)
{
    internal static StringPanelResponse From(InstallationDevice x) => new(x.Id, x.SerialNumber, x.Manufacturer, x.Model, x.PowerW);
}

/// <summary>A string with its installed panels. <see cref="TotalPowerW"/> is the sum of their rated power, computed on read.</summary>
public sealed record PanelStringResponse(
    Guid Id,
    Guid InstallationId,
    int Number,
    string? Name,
    int? MpptInput,
    int PanelCount,
    long TotalPowerW,
    IReadOnlyList<StringPanelResponse> Panels)
{
    internal static PanelStringResponse From(PanelString s, IEnumerable<InstallationDevice> panels)
    {
        var list = panels.OrderBy(p => p.RegisteredAt).ThenBy(p => p.Id).Select(StringPanelResponse.From).ToList();
        return new PanelStringResponse(s.Id, s.InstallationId, s.Number, s.Name, s.MpptInput, list.Count, list.Sum(p => (long)p.PowerW), list);
    }
}

/// <summary>Whole layout of an installation: strings ordered by number and installed panels not yet assigned to any string.</summary>
public sealed record StringLayoutResponse(
    Guid InstallationId,
    IReadOnlyList<PanelStringResponse> Strings,
    IReadOnlyList<StringPanelResponse> UnassignedPanels,
    long AssignedPowerW,
    long TotalPanelPowerW);
