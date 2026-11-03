using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Installations;

/// <summary>
/// A string of panels connected in series to one MPPT input of the inverter (#84).
/// <see cref="Number"/> is the string id the inverter reports in telemetry (<c>strings[].id</c>, ADR-185),
/// so measured production of a string can be matched with the panels assigned to it.
/// </summary>
public sealed class PanelString
{
    public const int NameMaxLength = 100;

    /// <summary>The telemetry contract allows at most 24 strings (and so MPPT inputs) per inverter.</summary>
    public const int MaxNumber = 24;

    private PanelString()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary>1..<see cref="MaxNumber"/>, unique within the installation; equals <c>strings[].id</c> in telemetry.</summary>
    public int Number { get; private set; }

    public string? Name { get; private set; }

    /// <summary>MPPT input of the inverter the string is wired to (several strings may share one input).</summary>
    public int? MpptInput { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static PanelString Create(Guid installationId, int number, string? name, int? mpptInput, DateTimeOffset now)
    {
        Validate(number, name, mpptInput);
        return new PanelString
        {
            Id = Guid.CreateVersion7(),
            InstallationId = installationId,
            Number = number,
            Name = Normalize(name),
            MpptInput = mpptInput,
            CreatedAt = now,
        };
    }

    public void Update(int number, string? name, int? mpptInput)
    {
        Validate(number, name, mpptInput);
        Number = number;
        Name = Normalize(name);
        MpptInput = mpptInput;
    }

    private static string? Normalize(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    private static void Validate(int number, string? name, int? mpptInput)
    {
        var errors = new Dictionary<string, string[]>();

        if (number is < 1 or > MaxNumber)
        {
            errors["Number"] = [$"String number must be between 1 and {MaxNumber}."];
        }

        if (name is not null && name.Trim().Length > NameMaxLength)
        {
            errors["Name"] = [$"Name is at most {NameMaxLength} characters."];
        }

        if (mpptInput is < 1 or > MaxNumber)
        {
            errors["MpptInput"] = [$"MPPT input must be between 1 and {MaxNumber}."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}
