using SolarOps.Api.Infrastructure.Errors;

namespace SolarOps.Api.Features.Installations;

/// <summary>Lifecycle of an installation (BPMN #65, #66; mapping #69).</summary>
public enum InstallationState
{
    Planned,
    ComponentsOrdered,
    ComponentsReceived,
    Mounting,
    MountingDone,
    Inspection,
    InspectionFailed,
    InspectionPassed,
    Commissioned,
    HandedOver,
}

/// <summary>
/// A PV plant being built for a customer. Bound to a contract and a supply point of other modules by id only.
/// State changes go through <see cref="TransitionTo"/> (or the named shortcuts) which reject invalid transitions.
/// </summary>
public sealed class Installation
{
    public const int NameMaxLength = 200;
    public const int NoteMaxLength = 2000;
    public const int AcceptedByMaxLength = 200;

    private static readonly IReadOnlyDictionary<InstallationState, InstallationState[]> AllowedTransitionMap =
        new Dictionary<InstallationState, InstallationState[]>
        {
            [InstallationState.Planned] = [InstallationState.ComponentsOrdered],
            [InstallationState.ComponentsOrdered] = [InstallationState.ComponentsReceived],
            [InstallationState.ComponentsReceived] = [InstallationState.Mounting],
            [InstallationState.Mounting] = [InstallationState.MountingDone],
            [InstallationState.MountingDone] = [InstallationState.Inspection],
            [InstallationState.Inspection] = [InstallationState.InspectionPassed, InstallationState.InspectionFailed],
            // Rework after a failed inspection returns the installation to mounting.
            [InstallationState.InspectionFailed] = [InstallationState.Mounting],
            [InstallationState.InspectionPassed] = [InstallationState.Commissioned],
            [InstallationState.Commissioned] = [InstallationState.HandedOver],
            [InstallationState.HandedOver] = [],
        };

    /// <summary>Targets that have their own step with extra data; the generic transition must not skip it.</summary>
    private static readonly IReadOnlyDictionary<InstallationState, string> DedicatedSteps =
        new Dictionary<InstallationState, string>
        {
            [InstallationState.InspectionPassed] = "recording a revision report",
            [InstallationState.InspectionFailed] = "recording a revision report",
            [InstallationState.Commissioned] = "commissioning with the acceptance protocol",
        };

    private readonly List<InstallationStateChange> _stateChanges = [];
    private readonly List<InstallationChecklistItem> _checklistItems = [];

    private Installation()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public Guid ContractId { get; private set; }

    public Guid SupplyPointId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Planned DC peak power (kWp) from the design; the actual value is derived from devices.</summary>
    public decimal? PlannedPowerKwp { get; private set; }

    public string? Note { get; private set; }

    public InstallationState State { get; private set; }

    public DateOnly? ScheduledStart { get; private set; }

    public DateOnly? ScheduledEnd { get; private set; }

    /// <summary>User ids of technicians assigned to the installation.</summary>
    public List<Guid> TechnicianIds { get; private set; } = [];

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset StateChangedAt { get; private set; }

    /// <summary>Latest revision report (#91); its result and validity are copied here for the commissioning check.</summary>
    public Guid? LastRevisionReportId { get; private set; }

    public RevisionResult? LastRevisionResult { get; private set; }

    public DateOnly? LastRevisionValidUntil { get; private set; }

    /// <summary>Day of commissioning from the acceptance protocol (#92).</summary>
    public DateOnly? CommissionedOn { get; private set; }

    public Guid? CommissionedBy { get; private set; }

    /// <summary>Person who took the plant over on the customer's side (signed the acceptance protocol).</summary>
    public string? HandoverAcceptedBy { get; private set; }

    public string? CommissioningNote { get; private set; }

    /// <summary>The plant is in operation and shown in monitoring (BPMN #66: MonitoringEnabled).</summary>
    public bool MonitoringEnabled { get; private set; }

    public DateTimeOffset? MonitoringEnabledAt { get; private set; }

    public IReadOnlyList<InstallationStateChange> StateChanges => _stateChanges;

    /// <summary>Stored states of checklist steps; steps without a row are not done (#90).</summary>
    public IReadOnlyList<InstallationChecklistItem> ChecklistItems => _checklistItems;

    /// <summary>Codes of required template steps that are not done yet.</summary>
    public IReadOnlyList<string> MissingRequiredChecklistSteps =>
        ChecklistTemplate.Steps
            .Where(step => step.Required && !_checklistItems.Any(item => item.StepCode == step.Code && item.Done))
            .Select(step => step.Code)
            .ToList();

    public bool IsChecklistComplete => MissingRequiredChecklistSteps.Count == 0;

    public IReadOnlyList<InstallationState> AllowedTransitions => AllowedTransitionMap[State];

    public bool IsClosed => State == InstallationState.HandedOver;

    public static Installation Create(
        Guid customerId,
        Guid contractId,
        Guid supplyPointId,
        string name,
        decimal? plannedPowerKwp,
        string? note,
        Guid createdBy,
        DateTimeOffset now)
    {
        var installation = new Installation
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customerId,
            ContractId = contractId,
            SupplyPointId = supplyPointId,
            Name = name.Trim(),
            PlannedPowerKwp = plannedPowerKwp,
            Note = note,
            State = InstallationState.Planned,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
            StateChangedAt = now,
        };
        installation._stateChanges.Add(InstallationStateChange.Create(installation.Id, null, InstallationState.Planned, createdBy, null, now));
        return installation;
    }

    public static bool CanTransition(InstallationState from, InstallationState to) => AllowedTransitionMap[from].Contains(to);

    public void UpdateDetails(string name, decimal? plannedPowerKwp, string? note, DateTimeOffset now)
    {
        EnsureNotClosed();
        Name = name.Trim();
        PlannedPowerKwp = plannedPowerKwp;
        Note = note;
        UpdatedAt = now;
    }

    public void Schedule(DateOnly start, DateOnly end, DateTimeOffset now)
    {
        if (end < start)
        {
            throw new DomainException("Scheduled end must not be before scheduled start.");
        }

        // #88: the date must not be in the past. An unchanged start may already be past (extending an ongoing window).
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        if (end < today || (start < today && start != ScheduledStart))
        {
            throw new DomainException("The installation date must not be in the past.");
        }

        if (State is InstallationState.Commissioned or InstallationState.HandedOver)
        {
            throw new DomainException($"Installation in state {State} can no longer be rescheduled.");
        }

        ScheduledStart = start;
        ScheduledEnd = end;
        UpdatedAt = now;
    }

    public void AssignTechnicians(IEnumerable<Guid> technicianIds, DateTimeOffset now)
    {
        EnsureNotClosed();
        TechnicianIds = technicianIds.Distinct().ToList();
        UpdatedAt = now;
    }

    /// <summary>
    /// Moves the installation to <paramref name="target"/>; throws <see cref="DomainException"/> (409) when not allowed.
    /// The revision result and commissioning have their own methods (<see cref="RecordRevision"/>, <see cref="Commission"/>).
    /// </summary>
    public void TransitionTo(InstallationState target, Guid changedBy, string? note, DateTimeOffset now)
    {
        EnsureCanTransition(target);
        if (DedicatedSteps.TryGetValue(target, out var step))
        {
            throw new DomainException($"State {target} is set by {step}, not by a plain state change.");
        }

        Move(target, changedBy, note, now);
    }

    /// <summary>Sets the state of one checklist step (#90); allowed only while mounting.</summary>
    public void SetChecklistStep(string stepCode, bool done, string? note, Guid by, DateTimeOffset now)
    {
        var step = ChecklistTemplate.Find(stepCode) ?? throw new NotFoundException("Checklist step", stepCode);
        if (State != InstallationState.Mounting)
        {
            throw new DomainException($"The checklist can be changed only while mounting (state is {State}).");
        }

        var item = _checklistItems.FirstOrDefault(x => x.StepCode == step.Code);
        if (item is null)
        {
            item = InstallationChecklistItem.Create(Id, step.Code, by, now);
            _checklistItems.Add(item);
        }

        item.Set(done, note, by, now);
        UpdatedAt = now;
    }

    /// <summary>
    /// Records the revision report (#91) and moves the installation to <see cref="InstallationState.InspectionPassed"/> or,
    /// for a failed revision, back to repairs (<see cref="InstallationState.InspectionFailed"/>, then rework).
    /// </summary>
    public void RecordRevision(RevisionReport report, Guid by, DateTimeOffset now)
    {
        if (State != InstallationState.Inspection)
        {
            throw new DomainException($"A revision report can be recorded only during inspection (state is {State}).");
        }

        if (report.InstallationId != Id)
        {
            throw new DomainException("The revision report belongs to another installation.");
        }

        LastRevisionReportId = report.Id;
        LastRevisionResult = report.Result;
        LastRevisionValidUntil = report.ValidUntil;
        var target = report.Result == RevisionResult.Passed ? InstallationState.InspectionPassed : InstallationState.InspectionFailed;
        Move(target, by, report.Findings, now);
    }

    /// <summary>
    /// Commissioning with the acceptance protocol (#92): needs a passed, still valid revision and an approved connection
    /// request (checked by the caller across modules). Turns monitoring on.
    /// </summary>
    public void Commission(DateOnly commissionedOn, string acceptedBy, string? note, bool connectionApproved, Guid by, DateTimeOffset now)
    {
        EnsureCanTransition(InstallationState.Commissioned);

        if (commissionedOn > DateOnly.FromDateTime(now.UtcDateTime))
        {
            throw new DomainException("The commissioning date must not be in the future.");
        }

        if (LastRevisionResult != RevisionResult.Passed)
        {
            throw new DomainException("Commissioning requires a passed revision report.");
        }

        if (LastRevisionValidUntil is { } validUntil && validUntil < commissionedOn)
        {
            throw new DomainException($"The revision expired on {validUntil:yyyy-MM-dd}; record a new revision report.");
        }

        if (!connectionApproved)
        {
            throw new DomainException("Commissioning requires an approved connection request of the distributor.");
        }

        CommissionedOn = commissionedOn;
        CommissionedBy = by;
        HandoverAcceptedBy = acceptedBy.Trim();
        CommissioningNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        MonitoringEnabled = true;
        MonitoringEnabledAt = now;
        Move(InstallationState.Commissioned, by, CommissioningNote, now);
    }

    private void EnsureCanTransition(InstallationState target)
    {
        if (!CanTransition(State, target))
        {
            throw new DomainException($"Installation cannot move from {State} to {target}.");
        }
    }

    private void Move(InstallationState target, Guid changedBy, string? note, DateTimeOffset now)
    {
        if (target == InstallationState.Mounting && (ScheduledStart is null || TechnicianIds.Count == 0))
        {
            throw new DomainException("Mounting can start only when the installation is scheduled and has at least one technician.");
        }

        if (target == InstallationState.MountingDone && MissingRequiredChecklistSteps is { Count: > 0 } missing)
        {
            throw new DomainException($"Mounting can be completed only when all required checklist steps are done. Missing: {string.Join(", ", missing)}.");
        }

        var from = State;
        State = target;
        StateChangedAt = now;
        UpdatedAt = now;
        _stateChanges.Add(InstallationStateChange.Create(Id, from, target, changedBy, note, now));
    }

    public void OrderComponents(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.ComponentsOrdered, by, null, now);

    public void ReceiveComponents(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.ComponentsReceived, by, null, now);

    public void StartMounting(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.Mounting, by, null, now);

    public void CompleteMounting(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.MountingDone, by, null, now);

    public void StartInspection(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.Inspection, by, null, now);

    public void Rework(Guid by, string? note, DateTimeOffset now) => TransitionTo(InstallationState.Mounting, by, note, now);

    public void HandOver(Guid by, DateTimeOffset now) => TransitionTo(InstallationState.HandedOver, by, null, now);

    private void EnsureNotClosed()
    {
        if (IsClosed)
        {
            throw new DomainException("Installation has been handed over and can no longer be changed.");
        }
    }
}

/// <summary>Audit record of one state change of an installation (#207: state_transitions).</summary>
public sealed class InstallationStateChange
{
    private InstallationStateChange()
    {
    }

    public Guid Id { get; private set; }

    public Guid InstallationId { get; private set; }

    public InstallationState? FromState { get; private set; }

    public InstallationState ToState { get; private set; }

    public Guid ChangedBy { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    public string? Note { get; private set; }

    internal static InstallationStateChange Create(
        Guid installationId, InstallationState? from, InstallationState to, Guid changedBy, string? note, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        InstallationId = installationId,
        FromState = from,
        ToState = to,
        ChangedBy = changedBy,
        ChangedAt = at,
        Note = note,
    };
}
