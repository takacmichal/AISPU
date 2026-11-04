using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Tickets;

/// <summary>Lifecycle of a technician visit (mapping #69).</summary>
public enum ServiceVisitState
{
    /// <summary>Date agreed, the technician is expected (#125).</summary>
    Planned,

    /// <summary>The technician is on site (#127).</summary>
    InProgress,

    /// <summary>The service protocol is written (#127); it can no longer be changed.</summary>
    Done,

    Cancelled,
}

/// <summary>Cause found on site; the code list of the service protocol (story #127). NoFaultFound feeds the false-alarm statistics (#129).</summary>
public enum VisitCause
{
    InverterFault,
    PanelFault,
    WiringFault,
    GridIssue,
    Shading,
    Monitoring,
    NoFaultFound,
    Other,
}

/// <summary>
/// The technician responsible for a ticket (story #124). A separate record because the ticket has no "assigned" state
/// (mapping #69 splits the states along the BPMN tasks); one row per ticket.
/// </summary>
public sealed class ServiceTicketAssignment
{
    private ServiceTicketAssignment()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public Guid AssignedBy { get; private set; }

    public DateTimeOffset AssignedAt { get; private set; }

    public static ServiceTicketAssignment Create(Guid ticketId, Guid technicianId, Guid by, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ServiceTicketId = ticketId,
        TechnicianId = technicianId,
        AssignedBy = by,
        AssignedAt = now,
    };

    public void Reassign(Guid technicianId, Guid by, DateTimeOffset now)
    {
        TechnicianId = technicianId;
        AssignedBy = by;
        AssignedAt = now;
    }
}

/// <summary>
/// Visit of a technician to the customer (BPMN #67 "Naplánovať výjazd technika"), its service protocol (#127) and
/// history of date changes (#125). State changes go through the methods, an invalid one throws <see cref="DomainException"/> (409).
/// </summary>
public sealed class ServiceVisit
{
    public const int MinEstimatedMinutes = 15;
    public const int MaxEstimatedMinutes = 12 * 60;
    public const int TextMaxLength = 4000;
    public const int ReasonMaxLength = 500;

    private readonly List<ServiceVisitReschedule> _reschedules = [];

    private ServiceVisit()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    public Guid TechnicianId { get; private set; }

    public ServiceVisitState State { get; private set; }

    public DateTimeOffset PlannedStart { get; private set; }

    public DateTimeOffset PlannedEnd { get; private set; }

    public int EstimatedMinutes { get; private set; }

    public Guid PlannedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public VisitCause? Cause { get; private set; }

    public string? Findings { get; private set; }

    public string? WorkDone { get; private set; }

    public DateTimeOffset? ArrivedAt { get; private set; }

    public DateTimeOffset? DepartedAt { get; private set; }

    public Guid? ReportedBy { get; private set; }

    public DateTimeOffset? ReportedAt { get; private set; }

    public IReadOnlyList<ServiceVisitReschedule> Reschedules => _reschedules;

    /// <summary>True while the visit blocks the calendar of its technician.</summary>
    public bool IsActive => State is ServiceVisitState.Planned or ServiceVisitState.InProgress;

    public static ServiceVisit Plan(Guid ticketId, Guid technicianId, DateTimeOffset start, int estimatedMinutes, Guid by, DateTimeOffset now)
    {
        ValidateWindow(start, estimatedMinutes, now);
        var utcStart = start.ToUniversalTime();
        return new ServiceVisit
        {
            Id = Guid.CreateVersion7(),
            ServiceTicketId = ticketId,
            TechnicianId = technicianId,
            State = ServiceVisitState.Planned,
            PlannedStart = utcStart,
            PlannedEnd = utcStart.AddMinutes(estimatedMinutes),
            EstimatedMinutes = estimatedMinutes,
            PlannedBy = by,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Changes the date of a planned visit; the reason is kept in <see cref="Reschedules"/> (story #125).</summary>
    public void Reschedule(DateTimeOffset start, int estimatedMinutes, string? reason, Guid by, DateTimeOffset now)
    {
        if (State != ServiceVisitState.Planned)
        {
            throw new DomainException($"A visit in state {State} cannot be rescheduled.");
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new RequestValidationException("Reason", "The Reason field is required.");
        }

        if (reason.Trim().Length > ReasonMaxLength)
        {
            throw new RequestValidationException("Reason", $"Reason is at most {ReasonMaxLength} characters.");
        }

        ValidateWindow(start, estimatedMinutes, now);
        _reschedules.Add(ServiceVisitReschedule.Create(Id, PlannedStart, PlannedEnd, reason, by, now));
        PlannedStart = start.ToUniversalTime();
        PlannedEnd = PlannedStart.AddMinutes(estimatedMinutes);
        EstimatedMinutes = estimatedMinutes;
        UpdatedAt = now;
    }

    public void Start(Guid by, DateTimeOffset now)
    {
        if (State != ServiceVisitState.Planned)
        {
            throw new DomainException($"A visit in state {State} cannot be started.");
        }

        State = ServiceVisitState.InProgress;
        StartedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Writes the service protocol and completes the visit; afterwards it is immutable (story #127).</summary>
    public void Report(
        VisitCause cause, string? findings, string? workDone, DateTimeOffset arrivedAt, DateTimeOffset departedAt, Guid by, DateTimeOffset now)
    {
        if (State != ServiceVisitState.InProgress)
        {
            throw new DomainException(State == ServiceVisitState.Done
                ? "The service protocol is already written and cannot be changed."
                : $"A visit in state {State} has no protocol; start it first.");
        }

        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(findings))
        {
            errors["Findings"] = ["The Findings field is required."];
        }

        if (string.IsNullOrWhiteSpace(workDone))
        {
            errors["WorkDone"] = ["The WorkDone field is required."];
        }

        if (!Enum.IsDefined(cause))
        {
            errors["Cause"] = ["Unknown cause."];
        }

        if (departedAt <= arrivedAt)
        {
            errors["DepartedAt"] = ["DepartedAt must be after ArrivedAt."];
        }
        else if (departedAt > now)
        {
            errors["DepartedAt"] = ["DepartedAt must not be in the future."];
        }

        if (arrivedAt > now)
        {
            errors["ArrivedAt"] = ["ArrivedAt must not be in the future."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }

        Cause = cause;
        Findings = findings!.Trim();
        WorkDone = workDone!.Trim();
        ArrivedAt = arrivedAt.ToUniversalTime();
        DepartedAt = departedAt.ToUniversalTime();
        ReportedBy = by;
        ReportedAt = now;
        State = ServiceVisitState.Done;
        UpdatedAt = now;
    }

    private static void ValidateWindow(DateTimeOffset start, int estimatedMinutes, DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (estimatedMinutes is < MinEstimatedMinutes or > MaxEstimatedMinutes)
        {
            errors["EstimatedMinutes"] = [$"EstimatedMinutes must be between {MinEstimatedMinutes} and {MaxEstimatedMinutes}."];
        }

        if (start < now)
        {
            errors["Start"] = ["Start must not be in the past."];
        }

        if (errors.Count > 0)
        {
            throw new RequestValidationException(errors);
        }
    }
}

/// <summary>Immutable log entry of a date change of a visit with its reason.</summary>
public sealed class ServiceVisitReschedule
{
    private ServiceVisitReschedule()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceVisitId { get; private set; }

    public DateTimeOffset PreviousStart { get; private set; }

    public DateTimeOffset PreviousEnd { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    internal static ServiceVisitReschedule Create(
        Guid visitId, DateTimeOffset previousStart, DateTimeOffset previousEnd, string reason, Guid by, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ServiceVisitId = visitId,
        PreviousStart = previousStart,
        PreviousEnd = previousEnd,
        Reason = reason.Trim(),
        ChangedBy = by,
        ChangedAt = now,
    };
}

/// <summary>Material used during a visit; bound to the ticket and the visit (story #128).</summary>
public sealed class ServiceVisitMaterial
{
    public const int NameMaxLength = 200;
    public const int UnitMaxLength = 16;

    private ServiceVisitMaterial()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    public Guid ServiceVisitId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = string.Empty;

    public static ServiceVisitMaterial Create(Guid ticketId, Guid visitId, string? name, decimal quantity, string? unit)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new RequestValidationException("Name", "The Name field is required.");
        }

        if (quantity <= 0)
        {
            throw new RequestValidationException("Quantity", "Quantity must be greater than zero.");
        }

        return new ServiceVisitMaterial
        {
            Id = Guid.CreateVersion7(),
            ServiceTicketId = ticketId,
            ServiceVisitId = visitId,
            Name = name.Trim(),
            Quantity = quantity,
            Unit = string.IsNullOrWhiteSpace(unit) ? "ks" : unit.Trim(),
        };
    }
}

/// <summary>
/// Link between a visit and a device replacement. The replacement itself (old device kept in history, serial number
/// validated) is done by the Installations module; this row records which visit and ticket it belongs to (story #128).
/// </summary>
public sealed class ServiceVisitDeviceReplacement
{
    private ServiceVisitDeviceReplacement()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    public Guid ServiceVisitId { get; private set; }

    public Guid OldDeviceId { get; private set; }

    public Guid NewDeviceId { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public Guid ReplacedBy { get; private set; }

    public DateTimeOffset ReplacedAt { get; private set; }

    public static ServiceVisitDeviceReplacement Create(
        Guid ticketId, Guid visitId, Guid oldDeviceId, Guid newDeviceId, string reason, Guid by, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ServiceTicketId = ticketId,
        ServiceVisitId = visitId,
        OldDeviceId = oldDeviceId,
        NewDeviceId = newDeviceId,
        Reason = reason.Trim(),
        ReplacedBy = by,
        ReplacedAt = now,
    };
}

/// <summary>Decision of the customer after the ticket was closed (story #129).</summary>
public enum TicketConfirmationDecision
{
    Confirmed,
    Reopened,
}

/// <summary>The customer's confirmation that the fault is fixed, or the reason for reopening. Never edited.</summary>
public sealed class ServiceTicketConfirmation
{
    public const int CommentMaxLength = 2000;

    private ServiceTicketConfirmation()
    {
    }

    public Guid Id { get; private set; }

    public Guid ServiceTicketId { get; private set; }

    public TicketConfirmationDecision Decision { get; private set; }

    public string? Comment { get; private set; }

    public Guid DecidedBy { get; private set; }

    public DateTimeOffset DecidedAt { get; private set; }

    public static ServiceTicketConfirmation Create(
        Guid ticketId, TicketConfirmationDecision decision, string? comment, Guid by, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        ServiceTicketId = ticketId,
        Decision = decision,
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
        DecidedBy = by,
        DecidedAt = now,
    };
}
