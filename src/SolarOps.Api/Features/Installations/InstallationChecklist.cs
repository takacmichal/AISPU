namespace SolarOps.Api.Features.Installations;

/// <summary>One step of the assembly checklist template (#90). Titles are shown to users, so they are Slovak.</summary>
public sealed record ChecklistStep(string Code, int Position, string Title, bool Required);

/// <summary>
/// Assembly checklist template (#90). It lives in code, the database keeps only the state of each step
/// (<see cref="InstallationChecklistItem"/>), so every installation, also an older one, gets the current template.
/// </summary>
public static class ChecklistTemplate
{
    public const int CodeMaxLength = 64;

    public static readonly IReadOnlyList<ChecklistStep> Steps =
    [
        new("SiteSafety", 1, "Zabezpečenie pracoviska a BOZP", Required: true),
        new("MountingStructure", 2, "Montáž nosnej konštrukcie", Required: true),
        new("PanelsMounted", 3, "Osadenie a upevnenie panelov", Required: true),
        new("DcCabling", 4, "Zapojenie stringov a DC kabeláž", Required: true),
        new("InverterMounted", 5, "Montáž a zapojenie invertora", Required: true),
        new("Protections", 6, "DC a AC ochrany, odpojovač, prepäťové ochrany", Required: true),
        new("AcConnection", 7, "Pripojenie do rozvádzača (AC strana)", Required: true),
        new("Grounding", 8, "Uzemnenie a pospájanie, ochrana pred bleskom", Required: true),
        new("StringMeasurements", 9, "Meranie izolačného odporu a polarity stringov", Required: true),
        new("PhotoDocumentation", 10, "Fotodokumentácia montáže", Required: false),
        new("SiteCleanup", 11, "Upratanie pracoviska", Required: false),
    ];

    public static ChecklistStep? Find(string code) =>
        Steps.FirstOrDefault(s => string.Equals(s.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>State of one checklist step of an installation; a missing row means "not done yet".</summary>
public sealed class InstallationChecklistItem
{
    public const int NoteMaxLength = 1000;

    private InstallationChecklistItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    /// <summary><see cref="ChecklistStep.Code"/> of the template step.</summary>
    public string StepCode { get; private set; } = string.Empty;

    public bool Done { get; private set; }

    public string? Note { get; private set; }

    /// <summary>Who changed the step last (#90: changes are timestamped).</summary>
    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    internal static InstallationChecklistItem Create(Guid installationId, string stepCode, Guid by, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        InstallationId = installationId,
        StepCode = stepCode,
        ChangedBy = by,
        ChangedAt = now,
    };

    internal void Set(bool done, string? note, Guid by, DateTimeOffset now)
    {
        Done = done;
        Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        ChangedBy = by;
        ChangedAt = now;
    }
}
