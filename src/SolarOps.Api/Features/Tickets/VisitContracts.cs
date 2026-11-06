using System.ComponentModel.DataAnnotations;
using SolarOps.Api.Features.Customers;
using SolarOps.Api.Features.Installations;
using SolarOps.Api.Infrastructure.Validation;

namespace SolarOps.Api.Features.Tickets;

/// <summary>Payload of <c>PUT /api/tickets/{id}/technician</c> (story #124).</summary>
public sealed record AssignTechnicianRequest : IValidatableObject
{
    /// <summary>User with the role Technician.</summary>
    [Required]
    public Guid? TechnicianId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        TechnicianId == Guid.Empty ? [new ValidationResult("The TechnicianId field must not be an empty id.", [nameof(TechnicianId)])] : [];
}

/// <summary>Payload of <c>POST /api/tickets/{id}/visits</c> (story #125): when the assigned technician comes.</summary>
public sealed record PlanVisitRequest
{
    [Required]
    public DateTimeOffset? Start { get; init; }

    /// <summary>Estimated duration in minutes.</summary>
    [Required, Range(ServiceVisit.MinEstimatedMinutes, ServiceVisit.MaxEstimatedMinutes)]
    public int? EstimatedMinutes { get; init; }
}

/// <summary>Payload of <c>PUT /api/visits/{id}/schedule</c>: a new date with the reason of the change.</summary>
public sealed record RescheduleVisitRequest
{
    [Required]
    public DateTimeOffset? Start { get; init; }

    [Required, Range(ServiceVisit.MinEstimatedMinutes, ServiceVisit.MaxEstimatedMinutes)]
    public int? EstimatedMinutes { get; init; }

    [Required, MaxLength(ServiceVisit.ReasonMaxLength)]
    public string? Reason { get; init; }
}

/// <summary>Payload of <c>POST /api/visits/{id}/report</c>: the service protocol (story #127).</summary>
public sealed record VisitReportRequest
{
    [Required, EnumDataType(typeof(VisitCause))]
    public VisitCause? Cause { get; init; }

    [Required, MaxLength(ServiceVisit.TextMaxLength)]
    public string? Findings { get; init; }

    [Required, MaxLength(ServiceVisit.TextMaxLength)]
    public string? WorkDone { get; init; }

    [Required]
    public DateTimeOffset? ArrivedAt { get; init; }

    [Required]
    public DateTimeOffset? DepartedAt { get; init; }
}

public sealed record VisitMaterialRequest
{
    [Required, MaxLength(ServiceVisitMaterial.NameMaxLength)]
    public string? Name { get; init; }

    [Required, Range(typeof(decimal), "0.001", "100000", ParseLimitsInInvariantCulture = true)]
    public decimal? Quantity { get; init; }

    [MaxLength(ServiceVisitMaterial.UnitMaxLength)]
    public string? Unit { get; init; }
}

/// <summary>Payload of <c>PUT /api/visits/{id}/materials</c>: replaces the whole list of used material (story #128).</summary>
public sealed record ReplaceVisitMaterialsRequest : IValidatableObject
{
    public const int MaxItems = 100;

    [Required, MaxLength(MaxItems)]
    public IReadOnlyList<VisitMaterialRequest>? Materials { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // The validation filter checks top-level members only, so items are checked here.
        foreach (var (item, index) in (Materials ?? []).Select((m, i) => (m, i)))
        {
            var prefix = $"{nameof(Materials)}[{index}]";
            if (item is null)
            {
                yield return new ValidationResult("Material must not be null.", [prefix]);
                continue;
            }

            foreach (var (member, messages) in DataAnnotationsValidator.Validate(item, validationContext))
            {
                foreach (var message in messages)
                {
                    yield return new ValidationResult(message, [$"{prefix}.{member}"]);
                }
            }

            if (item.Name is not null && string.IsNullOrWhiteSpace(item.Name))
            {
                yield return new ValidationResult("The Name field is required.", [$"{prefix}.{nameof(VisitMaterialRequest.Name)}"]);
            }
        }
    }
}

/// <summary>Payload of <c>POST /api/visits/{id}/device-replacements</c>: a device of the ticket's installation replaced during the visit.</summary>
public sealed record VisitDeviceReplacementRequest : IValidatableObject
{
    [Required]
    public Guid? DeviceId { get; init; }

    /// <summary>The new device; validated exactly like <c>POST /api/installations/{id}/devices/{deviceId}/replace</c>.</summary>
    [Required]
    public ReplaceDeviceRequest? Replacement { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Replacement is null)
        {
            yield break;
        }

        foreach (var (member, messages) in DataAnnotationsValidator.Validate(Replacement, validationContext))
        {
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [$"{nameof(Replacement)}.{member}"]);
            }
        }
    }
}

/// <summary>Payload of <c>POST /api/tickets/{id}/close</c>.</summary>
public sealed record CloseTicketRequest
{
    /// <summary>Internal note stored in the history; not shown to the customer.</summary>
    [MaxLength(ServiceTicket.NoteMaxLength)]
    public string? Note { get; init; }
}

/// <summary>Payload of <c>POST /api/tickets/{id}/confirm</c>.</summary>
public sealed record ConfirmTicketRequest
{
    [MaxLength(ServiceTicketConfirmation.CommentMaxLength)]
    public string? Comment { get; init; }
}

/// <summary>Payload of <c>POST /api/tickets/{id}/reopen</c>.</summary>
public sealed record ReopenTicketRequest
{
    [Required, MaxLength(ServiceTicketConfirmation.CommentMaxLength)]
    public string? Reason { get; init; }
}

public sealed record TicketAssignmentResponse(Guid TicketId, Guid TechnicianId, Guid AssignedBy, DateTimeOffset AssignedAt)
{
    internal static TicketAssignmentResponse From(ServiceTicketAssignment a) => new(a.ServiceTicketId, a.TechnicianId, a.AssignedBy, a.AssignedAt);
}

public sealed record VisitRescheduleResponse(DateTimeOffset PreviousStart, DateTimeOffset PreviousEnd, string Reason, Guid ChangedBy, DateTimeOffset ChangedAt);

/// <summary>Service protocol of a visit; <see cref="Findings"/> is internal and null for customers.</summary>
public sealed record VisitReportResponse(
    VisitCause Cause,
    string? Findings,
    string WorkDone,
    DateTimeOffset ArrivedAt,
    DateTimeOffset DepartedAt,
    Guid ReportedBy,
    DateTimeOffset ReportedAt);

public sealed record VisitMaterialResponse(Guid Id, string Name, decimal Quantity, string Unit);

public sealed record VisitDeviceReplacementResponse(
    Guid Id, Guid OldDeviceId, Guid NewDeviceId, string Reason, Guid ReplacedBy, DateTimeOffset ReplacedAt);

public sealed record VisitResponse(
    Guid Id,
    Guid TicketId,
    Guid? TechnicianId,
    ServiceVisitState State,
    DateTimeOffset PlannedStart,
    DateTimeOffset PlannedEnd,
    int EstimatedMinutes,
    DateTimeOffset? StartedAt,
    VisitReportResponse? Report,
    IReadOnlyList<VisitRescheduleResponse> Reschedules,
    IReadOnlyList<VisitMaterialResponse> Materials,
    IReadOnlyList<VisitDeviceReplacementResponse> DeviceReplacements);

/// <summary>Visits of one ticket and its assigned technician (null for customers).</summary>
public sealed record TicketVisitsResponse(Guid TicketId, Guid? AssignedTechnicianId, IReadOnlyList<VisitResponse> Visits);

/// <summary>Result of recording a used-material list.</summary>
public sealed record VisitDeviceReplacementResult(
    VisitDeviceReplacementResponse Link, DeviceResponse Replaced, DeviceResponse Replacement);

public sealed record CalendarCustomer(Guid Id, string Name, string? Phone, string Email);

/// <summary>One visit in a technician's calendar with the address and the contact of the customer (story #126).</summary>
public sealed record CalendarVisit(
    Guid VisitId,
    Guid TicketId,
    string TicketTitle,
    ServiceTicketPriority Priority,
    ServiceTicketState TicketState,
    ServiceVisitState State,
    DateTimeOffset Start,
    DateTimeOffset End,
    int EstimatedMinutes,
    Guid InstallationId,
    string? InstallationName,
    PostalAddress? Address,
    CalendarCustomer? Customer);

public sealed record TechnicianCalendarResponse(Guid TechnicianId, DateTimeOffset From, DateTimeOffset To, IReadOnlyList<CalendarVisit> Visits);

public sealed record TicketConfirmationResponse(
    Guid TicketId, TicketConfirmationDecision Decision, string? Comment, DateTimeOffset DecidedAt, ServiceTicketState TicketState);

/// <summary>State of a ticket after verify or close.</summary>
public sealed record TicketStateResponse(Guid TicketId, ServiceTicketState State, DateTimeOffset UpdatedAt);
