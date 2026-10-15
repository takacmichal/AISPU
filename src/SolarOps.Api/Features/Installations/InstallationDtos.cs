using System.ComponentModel.DataAnnotations;

namespace SolarOps.Api.Features.Installations;

/// <summary>Payload for creating an installation (state <c>Planned</c>).</summary>
public sealed record CreateInstallationRequest : IValidatableObject
{
    /// <summary>Customer (module Customers) the installation is built for.</summary>
    [Required]
    public Guid? CustomerId { get; init; }

    /// <summary>Signed contract (module Contracts); one installation per contract.</summary>
    [Required]
    public Guid? ContractId { get; init; }

    /// <summary>Supply point (odberné miesto) where the plant is connected.</summary>
    [Required]
    public Guid? SupplyPointId { get; init; }

    [Required, MaxLength(Installation.NameMaxLength)]
    public string? Name { get; init; }

    /// <summary>Planned DC power in kWp from the design (optional).</summary>
    [Range(0.01, 100000)]
    public decimal? PlannedPowerKwp { get; init; }

    [MaxLength(Installation.NoteMaxLength)]
    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (value, name) in new[] { (CustomerId, nameof(CustomerId)), (ContractId, nameof(ContractId)), (SupplyPointId, nameof(SupplyPointId)) })
        {
            if (value == Guid.Empty)
            {
                yield return new ValidationResult($"The {name} field must not be an empty id.", [name]);
            }
        }

        if (Name is not null && string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult("The Name field is required.", [nameof(Name)]);
        }
    }
}

/// <summary>Payload for updating descriptive fields of an installation.</summary>
public sealed record UpdateInstallationRequest : IValidatableObject
{
    [Required, MaxLength(Installation.NameMaxLength)]
    public string? Name { get; init; }

    [Range(0.01, 100000)]
    public decimal? PlannedPowerKwp { get; init; }

    [MaxLength(Installation.NoteMaxLength)]
    public string? Note { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Name is not null && string.IsNullOrWhiteSpace(Name))
        {
            yield return new ValidationResult("The Name field is required.", [nameof(Name)]);
        }
    }
}

/// <summary>Planned mounting window of an installation.</summary>
public sealed record ScheduleInstallationRequest : IValidatableObject
{
    [Required]
    public DateOnly? ScheduledStart { get; init; }

    [Required]
    public DateOnly? ScheduledEnd { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (ScheduledStart is { } start && ScheduledEnd is { } end && end < start)
        {
            yield return new ValidationResult("ScheduledEnd must not be before ScheduledStart.", [nameof(ScheduledEnd)]);
        }
    }
}

/// <summary>Replaces the set of technicians (user ids with role Technician) assigned to an installation.</summary>
public sealed record AssignTechniciansRequest : IValidatableObject
{
    [Required, MaxLength(20)]
    public IReadOnlyList<Guid>? TechnicianIds { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (TechnicianIds is not null && TechnicianIds.Contains(Guid.Empty))
        {
            yield return new ValidationResult("TechnicianIds must not contain an empty id.", [nameof(TechnicianIds)]);
        }
    }
}

/// <summary>Requested state change of an installation.</summary>
public sealed record InstallationTransitionRequest
{
    /// <summary>Target state; must be one of <c>allowedTransitions</c> of the installation.</summary>
    [Required]
    public InstallationState? TargetState { get; init; }

    [MaxLength(Installation.NoteMaxLength)]
    public string? Note { get; init; }
}

/// <summary>Installation as returned in lists.</summary>
public sealed record InstallationResponse(
    Guid Id,
    Guid CustomerId,
    Guid ContractId,
    Guid SupplyPointId,
    string Name,
    decimal? PlannedPowerKwp,
    string? Note,
    InstallationState State,
    IReadOnlyList<InstallationState> AllowedTransitions,
    DateOnly? ScheduledStart,
    DateOnly? ScheduledEnd,
    IReadOnlyList<Guid> TechnicianIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset StateChangedAt)
{
    internal static InstallationResponse From(Installation x) => new(
        x.Id, x.CustomerId, x.ContractId, x.SupplyPointId, x.Name, x.PlannedPowerKwp, x.Note, x.State,
        x.AllowedTransitions, x.ScheduledStart, x.ScheduledEnd, x.TechnicianIds, x.CreatedAt, x.UpdatedAt, x.StateChangedAt);
}

/// <summary>One entry of the installation state history.</summary>
public sealed record InstallationStateChangeResponse(
    InstallationState? FromState,
    InstallationState ToState,
    Guid ChangedBy,
    DateTimeOffset ChangedAt,
    string? Note);

/// <summary>Installation detail including its state history.</summary>
public sealed record InstallationDetailResponse(
    InstallationResponse Installation,
    IReadOnlyList<InstallationStateChangeResponse> StateHistory)
{
    internal static InstallationDetailResponse From(Installation x) => new(
        InstallationResponse.From(x),
        x.StateChanges
            .OrderBy(c => c.ChangedAt)
            .ThenBy(c => c.Id)
            .Select(c => new InstallationStateChangeResponse(c.FromState, c.ToState, c.ChangedBy, c.ChangedAt, c.Note))
            .ToList());
}

/// <summary>Filter of the installation list.</summary>
public sealed record InstallationQuery(
    InstallationState? State,
    Guid? CustomerId,
    Guid? ContractId,
    Guid? SupplyPointId,
    bool AssignedToMe);
